using System.Collections.Generic;

namespace NatController.Nat.Upnp;

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