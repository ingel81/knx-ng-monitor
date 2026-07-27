using Knx.Falcon.Sdk;
using Xunit;
using Xunit.Abstractions;

namespace KnxMonitor.Infrastructure.Tests;

/// <summary>
/// MANUAL diagnostic — opt-in via <c>KNX_ROUTING_PROBE=1</c>, same gate as
/// <see cref="RoutingHardwareProbe"/>.
///
/// Sends a KNXnet/IP SEARCH_REQUEST to the discovery multicast group and reports what answers.
/// This is the decisive test for "does multicast work on this machine at all": a search request
/// goes out over multicast and the reply comes back, so a hit proves both directions. It also
/// reveals which multicast group each router is actually configured for and which local interface
/// reached it — the usual culprit on Windows boxes with VPN / WSL / Hyper-V adapters.
///
/// Read-only with respect to the bus: discovery is a KNXnet/IP management exchange, no group
/// telegram is written.
/// </summary>
public class KnxIpDiscoveryProbe
{
    private readonly ITestOutputHelper _output;
    public KnxIpDiscoveryProbe(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Discovery_ListsKnxIpDevicesOnTheNetwork()
    {
        if (Environment.GetEnvironmentVariable("KNX_ROUTING_PROBE") != "1")
        {
            _output.WriteLine("SKIP: set KNX_ROUTING_PROBE=1 to run this against real hardware.");
            return;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var found = 0;

        await foreach (var device in KnxBus.DiscoverIpDevicesAsync(cts.Token))
        {
            found++;
            _output.WriteLine($"--- device {found} ---");
            _output.WriteLine($"  FriendlyName      : {device.FriendlyName}");
            _output.WriteLine($"  IndividualAddress : {device.IndividualAddress}");
            _output.WriteLine($"  ControlEndpoint   : {device.ControlEndpoint}");
            _output.WriteLine($"  DiscoveryEndpoint : {device.DiscoveryEndpoint}");
            _output.WriteLine($"  MulticastAddress  : {device.MulticastAddress}");
            _output.WriteLine($"  LocalIPAddress    : {device.LocalIPAddress}   <- interface that reached it");
            _output.WriteLine($"  MacAddress        : {device.MacAddress}");
            _output.WriteLine($"  MediumType        : {device.MediumType}");
            _output.WriteLine($"  Supports routing  : {device.Supports(Knx.Falcon.KnxnetIp.ServiceFamily.Routing, 1)}");
            _output.WriteLine($"  Supports tunneling: {device.Supports(Knx.Falcon.KnxnetIp.ServiceFamily.Tunneling, 1)}");
            _output.WriteLine($"  ConnectionString  : {device.ToConnectionString()}");
        }

        _output.WriteLine($"RESULT: {found} KNX/IP device(s) discovered.");
        Assert.True(found > 0,
            "no KNX/IP device answered the multicast search - multicast is not getting out of "
            + "this machine (firewall or wrong network interface), or there is no KNX/IP device "
            + "in this subnet");
    }
}
