using System;
using System.Threading.Tasks;
using Mono.Nat;

namespace UpnpController;

internal class Program
{
    private static async Task Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) =>
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(eventArgs.ExceptionObject.ToString());
            Console.ResetColor();
            Console.ReadKey(true);
            Environment.Exit((eventArgs.ExceptionObject as Upnp.UpnpException)?.Code ?? 1);
        };

        // foreach (IPAddress ipAddress in (await Dns.GetHostEntryAsync(Dns.GetHostName())).AddressList)
        // {
        //     Console.WriteLine(ipAddress);
        // }

        // Console.WriteLine(await Upnp.GetExternalIpAsync());

        // NetworkInterface[] adapters = NetworkInterface.GetAllNetworkInterfaces();
        // foreach (NetworkInterface adapter in adapters)
        // {
        //     if (adapter.NetworkInterfaceType != NetworkInterfaceType.Ethernet)
        //     {
        //         continue;
        //     }
        //     foreach (UnicastIPAddressInformation ip in adapter.GetIPProperties().UnicastAddresses)
        //     {
        //         if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        //         {
        //             Console.WriteLine(ip.Address.ToString());
        //         }
        //     }
        //
        // }
        // Console.WriteLine();

        Mono.Nat.NatUtility.StartDiscovery();
        Mono.Nat.NatUtility.DeviceFound += async (_, e) =>
        {
            if (e.Device.NatProtocol != NatProtocol.Upnp)
            {
                return;
            }

            // await e.Device.CreatePortMapAsync(new Mapping(Protocol.Udp, 11000, 11000));
            foreach (var item in await e.Device.GetAllMappingsAsync())
            {
                Console.WriteLine(item);
            }
        };

        Console.WriteLine(await Upnp.GetExternalIpAsync());

        // await Upnp.AddPortMappingAsync(11000, Upnp.MappingProtocol.UDP);
        // foreach (var mapping in await Upnp.GetAllPortMappingsAsync())
        // {
        //     Console.WriteLine(string.Join(", ", mapping.Select(pair => $"{pair.Key}={pair.Value}")));
        // }

        Console.WriteLine("Press any key to continue . . .");
        Console.ReadKey(true);
    }
}