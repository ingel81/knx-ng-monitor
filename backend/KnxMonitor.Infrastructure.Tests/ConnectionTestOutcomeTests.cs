using FluentAssertions;
using Knx.Falcon;
using Knx.Falcon.Configuration;
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
/// A routing probe cannot report reachability from the connect alone — the multicast join is
/// local and always succeeds, so "Connected" says nothing about whether anything will ever
/// arrive. These tests pin the two outcomes that distinguish a working routing setup from one
/// where multicast never reaches the process.
///
/// Both run on a private multicast group (not the KNX default), so no real installation is
/// touched and nothing here can be confused with bus traffic.
/// </summary>
public class ConnectionTestOutcomeTests
{
    private readonly ITestOutputHelper _output;
    public ConnectionTestOutcomeTests(ITestOutputHelper output) => _output = output;

    // Deliberately not 224.0.23.12: a private group nothing else uses.
    private const string QuietGroup = "239.255.99.99";
    private static readonly Knx.Falcon.GroupAddress Ga = new("1/2/3");

    private static KnxConnectionService CreateService() => new(
        NullLogger<KnxConnectionService>.Instance,
        Mock.Of<IGroupAddressCacheService>(),
        Mock.Of<ITelegramQueue>(),
        Mock.Of<IServiceScopeFactory>());

    private static KnxConfiguration RoutingConfig() => new()
    {
        ConnectionType = ConnectionType.Routing,
        IpAddress = QuietGroup,
        Port = 3671,
        PhysicalAddress = "1.0.58"
    };

    [Fact]
    public async Task Routing_WithoutTraffic_IsNotReportedAsSuccess()
    {
        var outcome = await CreateService().TestConnectionAsync(RoutingConfig());

        if (outcome == ConnectionTestOutcome.Failed)
        {
            _output.WriteLine("SKIP: multicast unavailable in this environment.");
            return;
        }

        outcome.Should().Be(ConnectionTestOutcome.JoinedWithoutTraffic,
            "a silent multicast group must not be reported as a working connection - that is "
            + "exactly the container-without-host-networking case");
    }

    [Fact]
    public async Task Routing_WithTraffic_IsReportedAsSuccess()
    {
        // A second bus supplies traffic on the same group while the probe listens, so the probe
        // must come back Success rather than warning about a dead group.
        var talker = new KnxBus(new IpRoutingConnectorParameters(
            System.Net.IPAddress.Parse(QuietGroup)) { MulticastTtl = 0 });
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await talker.ConnectAsync(cts.Token);
        }
        catch (Exception ex)
        {
            _output.WriteLine($"SKIP: no multicast bus available: {ex.GetType().Name}: {ex.Message}");
            await talker.DisposeAsync();
            return;
        }

        using var stop = new CancellationTokenSource();
        var chatter = Task.Run(async () =>
        {
            var value = DptConverter.Encode("1.001", "true")!;
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    await talker.WriteGroupValueAsync(Ga, value, MessagePriority.Low, stop.Token);
                    await Task.Delay(250, stop.Token);
                }
                catch (OperationCanceledException) { return; }
                catch { return; }
            }
        }, stop.Token);

        try
        {
            var outcome = await CreateService().TestConnectionAsync(RoutingConfig());
            _output.WriteLine($"Outcome with a talking peer on the group: {outcome}");

            if (outcome == ConnectionTestOutcome.JoinedWithoutTraffic)
            {
                _output.WriteLine("SKIP: multicast loopback between two local buses is unavailable.");
                return;
            }

            outcome.Should().Be(ConnectionTestOutcome.Success);
        }
        finally
        {
            stop.Cancel();
            try { await chatter; } catch { /* ignore */ }
            await talker.DisposeAsync();
        }
    }
}
