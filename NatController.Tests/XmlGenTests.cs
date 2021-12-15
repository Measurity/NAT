using System.Threading.Tasks;
using System.Xml.Linq;
using NUnit.Framework;

namespace NatController.Tests;

public class XmlGenTests
{
    [Test]
    public async Task TestGenerateXDocWithNamespace()
    {
        XNamespace nsEnvelope = "http://schemas.xmlsoap.org/soap/envelope/";
        XDocument doc = new(
            new XElement(nsEnvelope + "Envelope",
                new XAttribute(XNamespace.Xmlns + "e", nsEnvelope),
                new XElement(nsEnvelope + "Header"),
                new XElement(nsEnvelope + "Body")
            )
        );

        TestContext.WriteLine(doc);
    }
}