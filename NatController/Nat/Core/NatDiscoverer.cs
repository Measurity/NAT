using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace NatController.Nat.Core;

public abstract class NatDiscoverer<TDevice> : INatDiscover<TDevice> where TDevice : NatDevice
{
    private readonly ConcurrentDictionary<EndPoint, TDevice> discoveredDevices = new();
    private readonly object discoverTaskLocker = new();
    private Task<IEnumerable<TDevice>> discoverTaskCache;

    public event EventHandler<TDevice> DeviceDiscovered
    {
        add
        {
            deviceDiscovered += value;

            foreach (var pair in discoveredDevices)
            {
                value?.Invoke(null, pair.Value);
            }
        }
        remove => deviceDiscovered -= value;
    }

    private event EventHandler<TDevice> deviceDiscovered;

    public IEnumerable<TDevice> DiscoveredDevices => discoveredDevices.Values;

    public Task<IEnumerable<TDevice>> DiscoverAsync(int timeoutInMs = 60000)
    {
        // Singleton discovery task. Same task is reused to cache the result of the discovery broadcast.
        lock (discoverTaskLocker)
        {
            if (discoverTaskCache != null)
            {
                return discoverTaskCache;
            }

            return discoverTaskCache = DiscoveryUncachedAsync(timeoutInMs);
        }
    }

    protected abstract Task<IEnumerable<TDevice>> DiscoveryUncachedAsync(int timeoutInMs);

    protected virtual void OnDeviceDiscovered(TDevice e)
    {
        if (discoveredDevices.TryAdd(e.Address, e))
        {
            deviceDiscovered?.Invoke(this, e);
        }
    }

    public async Task<TResult> GetFirstAsync<TResult>(Func<TDevice, Task<TResult>> predicate) where TResult : class
    {
        TaskCompletionSource<TResult> interfaceDiscoveredSource = new();
        void Handler(object sender, TDevice device)
        {
            predicate(device)
                .ContinueWith(task =>
                {
                    if (interfaceDiscoveredSource.Task.IsCompleted)
                    {
                        return;
                    }
                    if (task.IsCompletedSuccessfully && task.Result is true or not null)
                    {
                        interfaceDiscoveredSource.SetResult(task.Result);
                    }
                });
        }

        try
        {
            DeviceDiscovered += Handler;

            // If the discover task completed synchronously then iterate it right now, else wait.  
            var discoverTask = DiscoverAsync();
            if (discoverTask.IsCompletedSuccessfully)
            {
                foreach (var pair in discoveredDevices)
                {
                    if (await predicate(pair.Value) is true or not null)
                    {
                        return (object)pair.Value as TResult;
                    }
                }
            }
            else
            {
                await Task.WhenAny(interfaceDiscoveredSource.Task, discoverTask).Unwrap();
                if (interfaceDiscoveredSource.Task.IsCompletedSuccessfully)
                {
                    return interfaceDiscoveredSource.Task.Result;
                }

                interfaceDiscoveredSource.SetCanceled();
            }

            return default(TResult);
        }
        finally
        {
            DeviceDiscovered -= Handler;
        }
    }
}