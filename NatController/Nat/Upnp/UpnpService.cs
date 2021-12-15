using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace NatController.Nat.Upnp;

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