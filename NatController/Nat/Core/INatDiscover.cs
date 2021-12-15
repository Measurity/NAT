using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NatController.Nat.Core;

public interface INatDiscover<TDevice> where TDevice : NatDevice
{
    /// <summary>
    ///     Event should fire when a NAT device is found.
    ///     New subscribers for the event should receive all previously found devices before new ones.
    /// </summary>
    event EventHandler<TDevice> DeviceDiscovered;

    /// <summary>
    ///     Sends a broadcast on all available networks, searching for any device that supports a NAT protocol.
    /// </summary>
    /// <param name="timeoutInMs">Timeout in milliseconds to wait on replies from the broadcast.</param>
    /// <returns>All devices found within the timeout.</returns>
    Task<IEnumerable<TDevice>> DiscoverAsync(int timeoutInMs = 60000);
}