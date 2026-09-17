using Hardware.KmBox;
using Smart.Adapters.KmBox;
using Smart.Runtime;
using Xunit;
namespace SampleProject.Tests;

public sealed class InputEndpointTests
{
    [Fact] public async Task EquivalentIpv4SpellingsCannotAcquireTwoOwnersOfTheSameEndpoint()
    {
        // Construction/disposal without InitializeAsync never connects or sends input.
        await using var first = new KmBoxInputDevice(new KmBoxOptions { IpAddress = "127.1", Port = 12345, Mac = "00000000" });
        await using var second = new KmBoxInputDevice(new KmBoxOptions { IpAddress = "127.0.0.1", Port = 12345, Mac = "11111111" });
        var leases = new InputLeaseRegistry();
        using var owner = leases.Acquire(first.DeviceId);
        Assert.Throws<InvalidOperationException>(() => leases.Acquire(second.DeviceId));
        Assert.Throws<InvalidOperationException>(() => leases.Acquire(" " + first.DeviceId + " "));
    }
}
