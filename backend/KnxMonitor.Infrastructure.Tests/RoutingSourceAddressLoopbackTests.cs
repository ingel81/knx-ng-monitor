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
/// End-to-end proof that a routing connection sends under the individual address configured in
/// Settings — over a real <see cref="KnxBus"/>, not just by inspecting the parameter object.
///
/// In routing mode there is no gateway lending us an address: the endpoint IS a bus participant
/// and puts its own address on every telegram. Before the fix the configured PhysicalAddress was
/// dropped and Falcon defaulted to 0.0.1, so two defaulting endpoints would clash on the bus.
///
/// Runs entirely in-process: both buses use MulticastTtl = 0, so nothing leaves this machine and
/// no real installation is touched. Skips (early return, logged) where the sandbox has no
/// multicast, exactly like <see cref="SecureLoopbackTests"/>.
/// </summary>
public class RoutingSourceAddressLoopbackTests
{
    private readonly ITestOutputHelper _output;
    public RoutingSourceAddressLoopbackTests(ITestOutputHelper output) => _output = output;

    private const string ConfiguredSource = "1.0.58";
    private static readonly Knx.Falcon.GroupAddress Ga = new("1/2/3");

    private static KnxConnectionService CreateService() => new(
        NullLogger<KnxConnectionService>.Instance,
        Mock.Of<IProjectCacheService>(),
        Mock.Of<ITelegramQueue>(),
        Mock.Of<IServiceScopeFactory>());

    private async Task<KnxBus?> TryConnectAsync(ConnectorParameters parameters, string role)
    {
        var bus = new KnxBus(parameters);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await bus.ConnectAsync(cts.Token);
            return bus.ConnectionState == BusConnectionState.Connected ? bus : null;
        }
        catch (Exception ex)
        {
            _output.WriteLine($"{role} bus connect failed (multicast likely unavailable): "
                            + $"{ex.GetType().Name}: {ex.Message}");
            try { await bus.DisposeAsync(); } catch { /* ignore */ }
            return null;
        }
    }

    [Fact]
    public async Task Routing_SendsUnderTheConfiguredIndividualAddress()
    {
        var config = new KnxConfiguration
        {
            ConnectionType = ConnectionType.Routing,
            IpAddress = "224.0.23.12",
            Port = 3671,
            PhysicalAddress = ConfiguredSource
        };

        // The production mapping, then TTL 0 so the telegram cannot leave this host.
        var senderParameters = (IpRoutingConnectorParameters)
            await CreateService().BuildConnectorParameters(config, keyringBlob: null);
        senderParameters.KnxAddress.Should().Be(new IndividualAddress(ConfiguredSource));
        senderParameters.MulticastTtl = 0;

        var receiverParameters = new IpRoutingConnectorParameters(senderParameters.MulticastAddress)
        {
            MulticastTtl = 0
        };

        var sender = await TryConnectAsync(senderParameters, "sender");
        if (sender is null) { _output.WriteLine("SKIP: no multicast bus available."); return; }
        var receiver = await TryConnectAsync(receiverParameters, "receiver");
        if (receiver is null) { await sender.DisposeAsync(); _output.WriteLine("SKIP: no second bus."); return; }

        try
        {
            var got = new TaskCompletionSource<IndividualAddress>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            receiver.GroupMessageReceived += (_, e) =>
            {
                if (e.DestinationAddress == Ga) got.TrySetResult(e.SourceAddress);
            };

            var value = DptConverter.Encode("1.001", "true");
            value.Should().NotBeNull();
            await sender.WriteGroupValueAsync(Ga, value!, MessagePriority.Low, CancellationToken.None);

            var completed = await Task.WhenAny(got.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            if (completed != got.Task)
            {
                _output.WriteLine("SKIP: telegram did not loop back (multicast loopback unavailable).");
                return;
            }

            var source = await got.Task;
            _output.WriteLine($"Received on the wire with source address: {source}");
            source.Should().Be(new IndividualAddress(ConfiguredSource),
                "routing must send under the address configured in Settings, not Falcon's default");
            source.Should().NotBe(IpRoutingConnectorParameters.DefaultKnxAddress);
        }
        finally
        {
            await receiver.DisposeAsync();
            await sender.DisposeAsync();
        }
    }
}
