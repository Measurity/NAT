using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using NatController.Nat.Core;

namespace NatController.Nat.Upnp;

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