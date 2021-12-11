using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using NUnit.Framework;

namespace UpnpController.Tests;

public class XmlParsingTests
{
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public async Task TestParseRemoteXml()
    {
        using HttpClient client = new();
        using HttpResponseMessage? response = await client.GetAsync("http://192.168.2.254:49152/wps_device.xml");
        XDocument doc = await XDocument.LoadAsync(await response.Content.ReadAsStreamAsync(), LoadOptions.None, CancellationToken.None);
        TestContext.WriteLine(doc.ToString());
    }

    [Test]
    public void TestParseInternetGatewayDeviceXml()
    {
        using Stream stream = Utils.GetContentFile("IGD.xml");
        XDocument doc = XDocument.Load(stream);
        var ns = doc.Root?.GetDefaultNamespace().NamespaceName ?? "";
        var services = doc.Descendants(XName.Get("service", ns))
            .Select(x => new
            {
                Id = x.Element(XName.Get("serviceId", ns)).Value,
                ControlUrl = x.Element(XName.Get("controlURL", ns)).Value,
                ScpdUrl = x.Element(XName.Get("SCPDURL", ns)).Value
            });

        foreach (var service in services)
        {
            TestContext.WriteLine(service);
        }
    }
}