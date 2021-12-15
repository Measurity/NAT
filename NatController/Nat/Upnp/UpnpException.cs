using System;
using System.Linq;
using System.Xml.Linq;

namespace NatController.Nat.Upnp;

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