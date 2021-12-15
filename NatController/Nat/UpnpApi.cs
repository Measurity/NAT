using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NatController.Nat.Upnp;
using NatController.Net;

namespace NatController.Nat;

/// <summary>
///     UPnP for internet gateway devices (UPnP-IGD): https://datatracker.ietf.org/doc/html/rfc6970
/// </summary>
public static class UpnpApi
{
    private static readonly UpnpDiscoverer discoverer = new();

    private static async Task<UpnpService> GetFirstServiceAsync(string serviceType)
    {
        return await discoverer.GetFirstAsync(async device =>
            (await device.GetServicesAsync()).FirstOrDefault(service => service.SimpleType.Equals(serviceType, StringComparison.InvariantCultureIgnoreCase)));
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
}