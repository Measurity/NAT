using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NatController.Nat.Core;
using NatController.Net;

namespace NatController.Nat.Discovery;

public class UpnpDiscoverer : NatDiscoverer<Upnp.UpnpDevice>
{
    /// <summary>
    ///     The standard IP that is used for UPnP broadcast. UPnP servers will listen to packets with this IP.
    /// </summary>
    private static readonly IPAddress UpnpAddress = IPAddress.Parse("239.255.255.250");

    /// <summary>
    ///     The UPnP message send in the UDP broadcast packet. UPnP servers on the network will respond to this message.
    /// </summary>
    private const string SearchDevicesMessage = "M-SEARCH * HTTP/1.1\r\nHOST:239.255.255.250:1900\r\nMAN:\"ssdp:discover\"\r\nST:ssdp:all\r\nMX:3\r\n\r\n";

    protected override async Task<IEnumerable<Upnp.UpnpDevice>> DiscoveryUncachedAsync(int timeoutInMs)
    {
        static async Task<Upnp.UpnpDevice> ReceiveNextAsync(IEnumerable<UdpClient> clients)
        {
            CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(10));
            while (!cancellation.IsCancellationRequested)
            {
                foreach (UdpClient client in clients)
                {
                    try
                    {
                        if (client.Available <= 0)
                        {
                            continue;
                        }
                        UdpReceiveResult result = await client.ReceiveAsync(cancellation.Token);
                        Upnp.UpnpDevice device = Upnp.UpnpDevice.FromUpnp(Encoding.UTF8.GetString(result.Buffer).Trim());
                        if (!device.Data.Any())
                        {
                            continue;
                        }

                        return device;
                    }
                    catch (TaskCanceledException)
                    {
                        // ignored
                    }
                }

                try
                {
                    await Task.Delay(10, cancellation.Token);
                }
                catch (TaskCanceledException)
                {
                    // ignored
                }
            }

            return null;
        }

        List<UdpClient> upnpClients = new();
        foreach (NetworkInterface networkInterface in NetHelper.GetInternetInterfaces())
        {
            foreach (UnicastIPAddressInformation unicastAddr in networkInterface.GetIPProperties().UnicastAddresses)
            {
                if (unicastAddr.Address.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }

                try
                {
                    UdpClient client = new(new IPEndPoint(unicastAddr.Address, 0));
                    client.Client.ReceiveTimeout = 5000;
                    upnpClients.Add(client);

                    client = new UdpClient();
                    client.Client.ReceiveTimeout = 5000;
                    client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.IpTimeToLive, 1);
                    client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(UpnpAddress, IPAddress.Any));
                    client.Client.Bind(new IPEndPoint(unicastAddr.Address, 1900));
                    upnpClients.Add(client);
                }
                catch (Exception)
                {
                    // ignored
                }
            }
        }

        // Send UPnP broadcast on all network interfaces.
        var sendBuffer = Encoding.UTF8.GetBytes(SearchDevicesMessage);
        foreach (UdpClient upnpClient in upnpClients)
        {
            await upnpClient.SendAsync(sendBuffer, sendBuffer.Length, new IPEndPoint(UpnpAddress, 1900));
        }

        // Receive UPnP responses.
        CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(timeoutInMs));
        while (!cancellation.IsCancellationRequested)
        {
            Upnp.UpnpDevice device = await ReceiveNextAsync(upnpClients);
            if (device == null)
            {
                await Task.Delay(10, cancellation.Token);
                continue;
            }

            if (".xml".Equals(new FileInfo(device.Location.AbsolutePath).Extension, StringComparison.InvariantCultureIgnoreCase))
            {
                OnDeviceDiscovered(device);
            }
        }

        // Cleanup.
        foreach (UdpClient client in upnpClients)
        {
            client.Dispose();
        }

        return DiscoveredDevices;
    }
}