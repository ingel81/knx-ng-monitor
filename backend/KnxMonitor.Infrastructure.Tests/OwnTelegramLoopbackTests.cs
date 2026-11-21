using FluentAssertions;
using Knx.Falcon.Configuration;
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
/// Falcon never raises GroupMessageReceived for telegrams its own bus sends. A write or read from
/// the app therefore reached the bus but showed up neither in the live view nor in the recording
/// (#49). Runs over a real routing bus with multicast TTL 0 (nothing leaves this host) and skips
/// where the sandbox has no multicast, like <see cref="SecureLoopbackTests"/>.
/// </summary>
[Collection(MulticastLoopbackCollection.Name)]
public class OwnTelegramLoopbackTests
{
    private readonly ITestOutputHelper _output;
    public OwnTelegramLoopbackTests(ITestOutputHelper output) => _output = output;

    private const string OwnAddress = "1.0.59";

    [Fact]
    public async Task OwnWriteAndRead_AreBroadcastAndQueued()
    {
        var queue = new Mock<ITelegramQueue>();
        var queued = new List<KnxTelegram>();
        queue.Setup(q => q.TryEnqueue(It.IsAny<KnxTelegram>()))
             .Callback<KnxTelegram>(queued.Add).Returns(true);

        var cache = new Mock<IProjectCacheService>();
        cache.Setup(c => c.GetByAddress("1/2/5"))
             .Returns(new GroupAddress { Id = 7, Address = "1/2/5", DatapointType = "DPST-1-1" });

        await using var service = new KnxConnectionService(
            NullLogger<KnxConnectionService>.Instance,
            cache.Object,
            queue.Object,
            Mock.Of<IServiceScopeFactory>())
        {
            ConfigureParametersForTests = p => ((IpRoutingConnectorParameters)p).MulticastTtl = 0
        };

        var broadcast = new List<KnxTelegram>();
        service.TelegramReceived += (_, t) => broadcast.Add(t);

        var connected = await service.ConnectAsync(new KnxConfiguration
        {
            ConnectionType = ConnectionType.Routing,
            IpAddress = "239.255.99.96",
            Port = 3671,
            PhysicalAddress = OwnAddress
        });
        if (!connected) { _output.WriteLine("SKIP: no multicast bus available."); return; }

        (await service.WriteGroupValueAsync("1/2/5", "1.001", "1")).Should().BeTrue();
        (await service.ReadGroupValueAsync("1/2/5")).Should().BeTrue();

        broadcast.Select(t => t.MessageType).Should().Equal(MessageType.Write, MessageType.Read);
        broadcast.Should().OnlyContain(t => t.DestinationAddress == "1/2/5" && t.SourceAddress == OwnAddress);
        broadcast.Should().OnlyContain(t => t.GroupAddressId == 7, "resolved like a received telegram");
        broadcast[0].ValueDecoded.Should().Be("On");
        queued.Should().HaveCount(2, "own telegrams belong in the recording too");
    }
}
