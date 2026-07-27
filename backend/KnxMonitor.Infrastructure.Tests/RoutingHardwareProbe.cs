using FluentAssertions;
using Knx.Falcon;
using Knx.Falcon.Sdk;
using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Enums;
using KnxMonitor.Core.Interfaces;
using KnxMonitor.Infrastructure.KnxConnection;
using KnxMonitor.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace KnxMonitor.Infrastructure.Tests;

/// <summary>
/// MANUAL hardware probe against a real KNX IP router — opt-in, skipped unless
/// <c>KNX_ROUTING_PROBE=1</c> is set, so CI and fresh clones are unaffected.
///
/// Strictly passive: it joins the routing multicast group and listens. Nothing is written to the
/// bus, so the configured source address is never put on the wire — safe to run even if the
/// address is not actually free in the installation.
///
/// <code>
/// KNX_ROUTING_PROBE=1 dotnet test KnxMonitor.Infrastructure.Tests \
///   --filter FullyQualifiedName~RoutingHardwareProbe \
///   --logger "console;verbosity=detailed"
/// </code>
///
/// Overrides: <c>KNX_ROUTING_GROUP</c> (default 224.0.23.12), <c>KNX_ROUTING_PORT</c> (3671),
/// <c>KNX_ROUTING_SOURCE</c> (1.0.58), <c>KNX_ROUTING_SECONDS</c> (20).
/// </summary>
public class RoutingHardwareProbe
{
    private readonly ITestOutputHelper _output;
    public RoutingHardwareProbe(ITestOutputHelper output) => _output = output;

    private static string Env(string name, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    [Fact]
    public async Task Routing_ReceivesTelegramsFromARealIpRouter()
    {
        if (Env("KNX_ROUTING_PROBE", "0") != "1")
        {
            _output.WriteLine("SKIP: set KNX_ROUTING_PROBE=1 to run this against real hardware.");
            return;
        }

        var config = new KnxConfiguration
        {
            ConnectionType = ConnectionType.Routing,
            IpAddress = Env("KNX_ROUTING_GROUP", "224.0.23.12"),
            Port = int.Parse(Env("KNX_ROUTING_PORT", "3671")),
            PhysicalAddress = Env("KNX_ROUTING_SOURCE", "1.0.58")
        };
        var seconds = int.Parse(Env("KNX_ROUTING_SECONDS", "20"));

        // The real production mapping — this is what the app would use after saving
        // "Routing" in Settings, not a hand-rolled parameter set.
        var service = new KnxConnectionService(
            NullLogger<KnxConnectionService>.Instance,
            Mock.Of<IGroupAddressCacheService>(),
            Mock.Of<ITelegramQueue>(),
            Mock.Of<IServiceScopeFactory>());
        var parameters = await service.BuildConnectorParameters(config, keyringBlob: null);

        _output.WriteLine($"Connector : {parameters.GetType().Name}");
        _output.WriteLine($"Parameters: {parameters}");
        _output.WriteLine($"Listening : {seconds}s on {config.IpAddress}:{config.Port} "
                        + $"as {config.PhysicalAddress} (passive - nothing is written)");

        await using var bus = new KnxBus(parameters);

        var received = 0;
        var sources = new HashSet<string>();
        bus.GroupMessageReceived += (_, e) =>
        {
            var n = Interlocked.Increment(ref received);
            lock (sources) { sources.Add(e.SourceAddress.ToString()); }
            if (n <= 25)
            {
                _output.WriteLine(
                    $"  [{n,3}] {e.SourceAddress} -> {e.DestinationAddress}  {e.EventType}  "
                    + $"{(e.Value is null ? "-" : Convert.ToHexString(e.Value.Value))}");
            }
        };

        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
        {
            await bus.ConnectAsync(cts.Token);
        }

        bus.ConnectionState.Should().Be(BusConnectionState.Connected,
            "joining the routing multicast group must succeed");
        _output.WriteLine($"Connected. Interface address: {bus.InterfaceConfiguration.IndividualAddress}");

        await Task.Delay(TimeSpan.FromSeconds(seconds));

        _output.WriteLine($"RESULT: {received} telegram(s) from {sources.Count} distinct source(s): "
                        + string.Join(", ", sources.OrderBy(s => s).Take(30)));
        received.Should().BeGreaterThan(0,
            "a live bus should produce traffic on the IP backbone within the listening window - "
            + "zero means multicast is not reaching this machine, or the router does not route "
            + "these telegrams to the backbone");
    }
}
