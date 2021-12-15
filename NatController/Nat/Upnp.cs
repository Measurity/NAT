using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using NatController.Nat.Core;
using NatController.Nat.Discovery;
using NatController.Net;

namespace NatController.Nat;

/// <summary>
///     UPnP for internet gateway devices (UPnP-IGD): https://datatracker.ietf.org/doc/html/rfc6970
/// </summary>
public static class Upnp
{
    private static readonly UpnpDiscoverer discoverer = new();

    private static async Task<UpnpService> GetFirstServiceAsync(string serviceType)
    {
        return await discoverer.GetFirstAsync(async device => (await device.GetServicesAsync()).FirstOrDefault(service => service.SimpleType.Equals(serviceType, StringComparison.InvariantCultureIgnoreCase)));
    }

    public static async Task<IPAddress> GetExternalIpAsync()
    {
        UpnpService service = await GetFirstServiceAsync("WANIPConnection");
        if (service?.Device == null)
        {
            return null;
        }
        UpnpResult result = await service.InvokeAsync("GetExternalIPAddress");
        if (!result.ResponseData.TryGetValue("NewExternalIPAddress", out var ipStr))
        {
            return null;
        }

        IPAddress.TryParse(ipStr, out IPAddress ip);
        return ip;
    }

    public static async Task<IEnumerable<IReadOnlyDictionary<string, string>>> GetAllPortMappingsAsync()
    {
        UpnpService service = await GetFirstServiceAsync("WANIPConnection");
        if (service?.Device == null)
        {
            return Array.Empty<Dictionary<string, string>>();
        }

        List<IReadOnlyDictionary<string, string>> mappings = new(1000);
        for (var i = 0; i < 1000; i++)
        {
            try
            {
                UpnpResult result = await service.InvokeAsync("GetGenericPortMappingEntry", ("NewPortMappingIndex", i));
                mappings.Add(result.ResponseData);
            }
            catch (UpnpException ex)
            {
                if (ex.Code != 713)
                {
                    throw;
                }
            }
        }
        return mappings;
    }

    public static async Task<UpnpResult> DeletePortMappingAsync(ushort port, MappingProtocol protocol, IPAddress lanIp = null)
    {
        lanIp ??= NetHelper.GetLanIp();

        UpnpService service = await GetFirstServiceAsync("WANIPConnection");
        if (service?.Device == null)
        {
            return false;
        }

        try
        {
            UpnpResult existed = await GetPortMappingAsync(port, protocol, lanIp);
            UpnpResult result = await service.InvokeAsync("DeletePortMapping", ("NewRemoteHost", lanIp), ("NewExternalPort", port),
                ("NewProtocol", protocol.ToString()));
            return new UpnpResult(service, "DeletePortMapping", result.ResponseData, existed.Success);
        }
        catch (UpnpException ex)
        {
            if (ex.Code == 714) // Entry does not exist
            {
                return false;
            }
            throw;
        }
    }

    public static async Task<UpnpResult> GetPortMappingAsync(ushort port, MappingProtocol protocol, IPAddress lanIp = null)
    {
        lanIp ??= NetHelper.GetLanIp();

        UpnpService service = await GetFirstServiceAsync("WANIPConnection");
        if (service?.Device == null)
        {
            return false;
        }

        try
        {
            return await service.InvokeAsync("GetSpecificPortMappingEntry", ("NewRemoteHost", lanIp), ("NewExternalPort", port),
                ("NewProtocol", protocol.ToString()));
        }
        catch (UpnpException ex)
        {
            if (ex.Code == 714) // Entry does not exist
            {
                return false;
            }
            throw;
        }
    }

    public static async Task<UpnpResult> AddPortMappingAsync(ushort port, MappingProtocol protocol = MappingProtocol.BOTH, IPAddress lanIp = null, string description = null)
    {
        string CreateDefaultDescription()
        {
            string str;
            try
            {
                using Process proc = Process.GetCurrentProcess();
                str = proc.ProcessName;
            }
            catch
            {
                str = "Nitrox UPnP";
            }

            if (str.Length > 12)
            {
                str = str.Substring(0, 12);
            }
            return string.Format($"{str} {protocol} {port}");
        }


        lanIp ??= NetHelper.GetLanIp();

        UpnpService service = await GetFirstServiceAsync("WANIPConnection");
        if (service?.Device == null)
        {
            return false;
        }

        return await service.InvokeAsync("AddPortMapping",
            ("NewRemoteHost", ""),
            ("NewInternalClient", lanIp),
            ("NewInternalPort", port),
            ("NewExternalPort", port),
            ("NewProtocol", protocol.ToString()),
            ("NewPortMappingDescription", string.IsNullOrWhiteSpace(description) ? CreateDefaultDescription() : description),
            ("NewLeaseDuration", 0),
            ("NewEnabled", "1")
        );
    }

    /// <summary>
    ///     A "device" that is listening to UPnP broadcast and is exposing their API. One physical device can expose multiple UPnP "devices".
    /// </summary>
    public class UpnpDevice : NatDevice
    {
        private static readonly Regex ServiceKeyValueRegex = new(@"([0-1A-Za-z\-]+): ([^\r\n]+)", RegexOptions.Compiled | RegexOptions.Multiline);

        private XDocument xmlDocCache;
        private readonly object xmlDocCacheLocker = new();

        public string ServerName => Data["SERVER"];
        public Uri Location => new(Data["LOCATION"]);
        public ReadOnlyDictionary<string, string> Data { get; set; }

        public static UpnpDevice FromUpnp(string infoString)
        {
            UpnpDevice info = new();

            // Aggregate data into dictionary.
            Dictionary<string, string> data = new();
            foreach (Match match in ServiceKeyValueRegex.Matches(infoString))
            {
                var key = match.Groups[1].Value;
                var value = match.Groups[2].Value;
                data[key] = value;
            }
            info.Data = new ReadOnlyDictionary<string, string>(data);
            return info;
        }

        public async Task<IEnumerable<UpnpService>> GetServicesAsync()
        {
            XDocument doc = null;
            lock (xmlDocCacheLocker)
            {
                if (xmlDocCache != null)
                {
                    doc = xmlDocCache;
                }
            }

            if (doc == null)
            {
                using HttpClient client = new();
                using HttpResponseMessage response = await client.GetAsync(Location);
                doc = XDocument.Load(await response.Content.ReadAsStreamAsync(), LoadOptions.None);
                lock (xmlDocCacheLocker)
                {
                    xmlDocCache = doc;
                }
            }
            
            var ns = doc.Root?.GetDefaultNamespace().NamespaceName ?? "";
            return doc.Descendants(XName.Get("service", ns))
                .Select(x =>
                {
                    var serviceId = x.Element(XName.Get("serviceId", ns))?.Value;
                    var serviceType = x.Element(XName.Get("serviceType", ns))?.Value ?? serviceId;
                    var controlUrl = x.Element(XName.Get("controlURL", ns))?.Value;
                    var scpdUrl = x.Element(XName.Get("SCPDURL", ns))?.Value;
                    return new UpnpService(this, serviceId, serviceType, controlUrl, scpdUrl);
                });
        }

        public override string ToString()
        {
            return string.Join(Environment.NewLine, Data.Select(i => $"{i.Key}: {i.Value}"));
        }

        public override EndPoint Address => new IPEndPoint(IPAddress.Parse(Location.Host), Location.Port);
    }

    /// <summary>
    ///     A UPnP service that has a SOAP API.
    /// </summary>
    public record UpnpService(UpnpDevice Device, string Id, string Type, string ControlUrl, string ScpdUrl)
    {
        public string Id { get; set; } = Id ?? throw new ArgumentNullException(nameof(Id));
        public string Type { get; init; } = Type ?? throw new ArgumentNullException(nameof(Type));
        public string SimpleType => Regex.Match(Type, @"^.*:([a-zA-Z]+[a-zA-Z0-9]+)(?::|$)").Groups[1].Value;
        public string ControlUrl { get; init; } = ControlUrl ?? throw new ArgumentNullException(nameof(ControlUrl));
        public string ScpdUrl { get; init; } = ScpdUrl ?? throw new ArgumentNullException(nameof(ScpdUrl));

        /// <summary>
        ///     Sends a SOAP request to run the action on the service with the given arguments. Parsing the returned XML response as a key-value dictionary.
        /// </summary>
        public async Task<UpnpResult> InvokeAsync(string action, params (string name, object value)[] args)
        {
            if (Device == null)
            {
                throw new InvalidOperationException("Device info is required for service to be invoked");
            }

            // Generate XML body for request.
            XNamespace nsEnvelope = "http://schemas.xmlsoap.org/soap/envelope/";
            XNamespace nsAction = Type;
            var xActionBody = new List<object>();
            xActionBody.Add(new XAttribute(XNamespace.Xmlns + "a", nsAction));
            xActionBody.AddRange(args.Select(a => new XElement(a.name, a.value)));
            XDocument requestDoc = new(
                new XElement(nsEnvelope + "Envelope",
                    new XAttribute(XNamespace.Xmlns + "e", nsEnvelope),
                    new XElement(nsEnvelope + "Header"),
                    new XElement(nsEnvelope + "Body",
                        new XElement(nsAction + action, xActionBody)
                    )
                )
            );

            // Prepare SOAP request against the UPnP service.
            using HttpClient client = new();
            using HttpRequestMessage request = new(HttpMethod.Post, new Uri(Device.Location, ControlUrl));
            request.Headers.Add("SOAPAction", $"{Type}#{action}");
            request.Content = new StringContent(requestDoc.ToString(), Encoding.UTF8, "text/xml");
            using HttpResponseMessage response = await client.SendAsync(request);

            // Parse XML response.
            XDocument doc = XDocument.Load(await response.Content.ReadAsStreamAsync(), LoadOptions.None);
            UpnpException error = UpnpException.From(doc);
            if (error != null)
            {
                throw error;
            }

            // The first element with an XML namespace contains the body, search it and parse the children elements as key-value. 
            return new UpnpResult(this, action, doc.Descendants()
                .First(x => x.GetPrefixOfNamespace(Type) is not null)
                .Elements()
                .ToDictionary(x => x.Name.LocalName, x => x.Value));
        }
    }

    /// <summary>
    ///     Available port mappings when port forwarding.
    /// </summary>
    public enum MappingProtocol
    {
        UDP,
        TCP,
        BOTH
    }

    public class UpnpException : Exception
    {
        public int Code { get; init; }

        public UpnpException(int code, string message) : base(message)
        {
            Code = code;
        }

        public static UpnpException From(XDocument doc)
        {
            XNamespace ns = "http://schemas.xmlsoap.org/soap/envelope/";
            XElement xError = doc.Root?.Element(ns + "Body")?.Element(ns + "Fault")?.Element("detail")?.Elements().FirstOrDefault(x => x.Name.LocalName == "UPnPError");
            if (xError == null)
            {
                return null;
            }

            XNamespace nsError = xError.GetDefaultNamespace();
            int.TryParse(xError.Element(nsError + "errorCode")?.Value, out var errorCode);
            return new UpnpException(errorCode, xError.Element(nsError + "errorDescription")?.Value);
        }

        public override string ToString()
        {
            return $"{base.ToString()}, {nameof(Code)}: {Code}";
        }
    }

    /// <summary>
    ///     Wrapper for UPnP responses. If <see cref="Success" /> is true then the <see cref="ResponseData" /> will have items.
    /// </summary>
    public record UpnpResult(UpnpService Service, string Action, IReadOnlyDictionary<string, string> ResponseData, bool Success = true)
    {
        public static implicit operator UpnpResult(bool result)
        {
            return new UpnpResult(null, null, new Dictionary<string, string>(), result);
        }

        public static implicit operator bool(UpnpResult result)
        {
            return result.Success;
        }
    }
}