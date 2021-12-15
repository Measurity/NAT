using System.Net;

namespace NatController.Nat.Core;

public abstract class NatDevice
{
    public abstract EndPoint Address { get; }
}