using FluentAssertions;
using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Enums;
using KnxMonitor.Core.Interfaces;
using KnxMonitor.Core.Services;
using KnxMonitor.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace KnxMonitor.Infrastructure.Tests;

/// <summary>
/// The reconnect policy from #22: link loss wakes the worker right away instead of waiting for the
/// 10 s poll, failed attempts back off, and the worker stays out while Falcon recovers the tunnel.
/// </summary>
public class KnxAutoConnectWorkerTests
{
    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(4, 8)]
    [InlineData(5, 10)]
    [InlineData(50, 10)]
    public void NextDelay_BacksOffUpToThePollInterval(int failures, int expectedSeconds)
    {
        KnxAutoConnectWorker.NextDelay(failures, recovering: false)
            .Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Fact]
    public void NextDelay_ChecksAgainSoonWhileFalconRecovers()
    {
        KnxAutoConnectWorker.NextDelay(0, recovering: true).Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task LinkLost_TriggersAReconnectWithoutWaitingForThePoll()
    {
        var knx = new FakeKnx { Connected = true };
        using var worker = CreateWorker(knx);
        await worker.StartAsync(default);
        try
        {
            // Startup delay is 3 s; the link is up, so the first round does nothing.
            await Task.Delay(TimeSpan.FromSeconds(3.5));
            knx.ConnectCalls.Should().Be(0);

            knx.Connected = false;
            knx.RaiseLinkLost();

            // Woken by the event plus a 1 s settle delay, far below the 10 s poll.
            await WaitUntil(() => knx.ConnectCalls > 0, TimeSpan.FromSeconds(4));
            knx.ConnectCalls.Should().Be(1);
        }
        finally
        {
            await worker.StopAsync(default);
        }
    }

    [Fact]
    public async Task StaysOutWhileFalconIsRecoveringTheTunnel()
    {
        var knx = new FakeKnx { Connected = false, Recovering = true };
        using var worker = CreateWorker(knx);
        await worker.StartAsync(default);
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            knx.ConnectCalls.Should().Be(0, "a second bus next to Falcon's own reconnect would cost a tunnel slot");

            knx.Recovering = false; // grace period over
            await WaitUntil(() => knx.ConnectCalls > 0, TimeSpan.FromSeconds(3));
        }
        finally
        {
            await worker.StopAsync(default);
        }
    }

    [Fact]
    public async Task AFlappingLinkIsRetriedWithBackoff()
    {
        // Every connect succeeds but the link drops right away: without a stable-link rule this
        // reconnected once a second forever.
        var knx = new FakeKnx { Connected = false, DropRightAfterConnect = true };
        using var worker = CreateWorker(knx);
        await worker.StartAsync(default);
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(13)); // 3 s startup + 10 s
            knx.ConnectCalls.Should().BeInRange(2, 5, "attempts back off 1, 2, 4, 8 s instead of every second");
        }
        finally
        {
            await worker.StopAsync(default);
        }
    }

    private static KnxAutoConnectWorker CreateWorker(IKnxConnectionService knx)
    {
        var repo = new Mock<IKnxConfigurationRepository>();
        repo.Setup(r => r.GetAllAsync()).ReturnsAsync(new[]
        {
            new KnxConfiguration { Id = 1, IpAddress = "192.168.10.60", AutoConnect = true, IsActive = true }
        });

        var services = new ServiceCollection();
        services.AddSingleton(repo.Object);
        var provider = services.BuildServiceProvider();

        return new KnxAutoConnectWorker(knx, provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<KnxAutoConnectWorker>.Instance);
    }

    private static async Task WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("condition not met in time");
            }
            await Task.Delay(50);
        }
    }

    private sealed class FakeKnx : IKnxConnectionService
    {
        private int _connectCalls;
        public volatile bool Connected;
        public volatile bool Recovering;
        public volatile bool DropRightAfterConnect;
        public int ConnectCalls => Volatile.Read(ref _connectCalls);

#pragma warning disable CS0067 // TelegramReceived is part of the contract but irrelevant here
        public event EventHandler<KnxTelegram>? TelegramReceived;
#pragma warning restore CS0067
        public event EventHandler? LinkLost;
        public void RaiseLinkLost() => LinkLost?.Invoke(this, EventArgs.Empty);

        public bool IsConnected => Connected;
        public bool IsRecovering => Recovering;
        public KnxLinkState State => Connected ? KnxLinkState.Connected : KnxLinkState.Disconnected;
        public bool ManualDisconnect => false;
        public bool TestInProgress => false;
        public DateTime? ConnectedSince => null;
        public DateTime? LastDisconnectedAt => null;

        public Task<bool> ConnectAsync(KnxConfiguration configuration)
        {
            Interlocked.Increment(ref _connectCalls);
            Connected = !DropRightAfterConnect;
            if (DropRightAfterConnect)
            {
                _ = Task.Run(async () => { await Task.Delay(50); RaiseLinkLost(); });
            }
            return Task.FromResult(true);
        }

        public Task<ConnectionTestOutcome> TestConnectionAsync(KnxConfiguration configuration)
            => Task.FromResult(ConnectionTestOutcome.Failed);
        public Task DisconnectAsync() => Task.CompletedTask;
        public Task<bool> WriteGroupValueAsync(string address, string? dptType, string value) => Task.FromResult(false);
        public Task<bool> ReadGroupValueAsync(string address) => Task.FromResult(false);
        public Task<KnxConfiguration?> GetActiveConfigurationAsync() => Task.FromResult<KnxConfiguration?>(null);
    }
}
