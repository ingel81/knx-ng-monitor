using FluentAssertions;
using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Enums;
using KnxMonitor.Core.Interfaces;
using KnxMonitor.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace KnxMonitor.Infrastructure.Tests;

/// <summary>
/// The history writer against a real SQLite file: immediate mode, the buffered mode from #37,
/// and that a shutdown writes what is still buffered instead of dropping it.
/// </summary>
public class TelegramPersistenceServiceTests : IDisposable
{
    private readonly SqliteTestDb _db = new();
    private readonly FakeSettings _settings = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task ImmediateMode_WritesRightAway()
    {
        var svc = CreateService();
        await svc.StartAsync(default);
        try
        {
            Enqueue(svc, 3);
            await WaitForCount(3, TimeSpan.FromSeconds(3));
        }
        finally
        {
            await svc.StopAsync(default);
        }
    }

    [Fact]
    public async Task BufferedMode_HoldsTelegramsUntilTheIntervalIsOver()
    {
        _settings.Current.HistoryWriteIntervalSeconds = 3;
        var svc = CreateService();
        await svc.StartAsync(default);
        try
        {
            Enqueue(svc, 5);
            await Task.Delay(TimeSpan.FromSeconds(1.5));
            (await CountAsync()).Should().Be(0, "the interval has not passed yet");

            await WaitForCount(5, TimeSpan.FromSeconds(4));
        }
        finally
        {
            await svc.StopAsync(default);
        }
    }

    [Fact]
    public async Task Shutdown_WritesWhatIsStillBuffered()
    {
        _settings.Current.HistoryWriteIntervalSeconds = 600;
        var svc = CreateService();
        await svc.StartAsync(default);

        Enqueue(svc, 7);
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        (await CountAsync()).Should().Be(0);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await svc.StopAsync(timeout.Token);

        (await CountAsync()).Should().Be(7, "stopping must not lose a ten-minute buffer");
    }

    [Fact]
    public async Task BufferedMode_WritesHeartbeatsTogetherWithTheirTelegrams()
    {
        // After a power cut a heartbeat on disk must not vouch for telegrams that never got there.
        _settings.Current.HistoryWriteIntervalSeconds = 3;
        var svc = CreateService();
        await svc.StartAsync(default);
        try
        {
            Enqueue(svc, 2);
            svc.TryEnqueueHeartbeat(new MonitorHeartbeat { Timestamp = DateTime.UtcNow, State = KnxLinkState.Connected })
                .Should().Be(HeartbeatEnqueueResult.Enqueued);
            await Task.Delay(TimeSpan.FromSeconds(1.5));
            (await BeatCountAsync()).Should().Be(0, "the beat waits for the flush of its telegrams");

            await WaitForCount(2, TimeSpan.FromSeconds(4));
            (await BeatCountAsync()).Should().Be(1);
        }
        finally
        {
            await svc.StopAsync(default);
        }
    }

    [Fact]
    public void ImmediateMode_LeavesHeartbeatsToTheCaller()
    {
        var svc = CreateService();
        svc.TryEnqueueHeartbeat(new MonitorHeartbeat { Timestamp = DateTime.UtcNow }).Should().Be(HeartbeatEnqueueResult.NotBuffering);
    }

    private async Task<int> BeatCountAsync()
    {
        using var scope = _db.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IMonitorHeartbeatRepository>();
        return (await repo.GetRangeAsync(DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1))).Count;
    }

    private TelegramPersistenceService CreateService() => new(
        _db.Services.GetRequiredService<IServiceScopeFactory>(),
        _settings,
        NullLogger<TelegramPersistenceService>.Instance);

    private static void Enqueue(TelegramPersistenceService svc, int count)
    {
        for (var i = 0; i < count; i++)
        {
            svc.TryEnqueue(new KnxTelegram
            {
                Timestamp = DateTime.UtcNow,
                SourceAddress = "1.1.1",
                DestinationAddress = $"1/1/{i}",
                MessageType = MessageType.Write,
                Value = new byte[] { 1 }
            }).Should().BeTrue();
        }
    }

    private async Task<int> CountAsync()
    {
        using var scope = _db.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ITelegramRepository>().GetTotalCountAsync();
    }

    private async Task WaitForCount(int expected, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (await CountAsync() < expected)
        {
            if (DateTime.UtcNow > deadline)
            {
                (await CountAsync()).Should().Be(expected, "the telegrams should have been written by now");
            }
            await Task.Delay(100);
        }
        (await CountAsync()).Should().Be(expected);
    }

    private sealed class FakeSettings : IRecordingSettingsProvider
    {
        public RecordingSettings Current { get; } = new();
        public int HotBufferMaxCount => Current.HotBufferMaxCount;
        public bool ArchiveEnabled => Current.ArchiveEnabled;
        public int? ArchiveRetentionDays => Current.ArchiveRetentionDays;
        public int HistoryWriteIntervalSeconds => Current.HistoryWriteIntervalSeconds;
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(RecordingSettings updated, CancellationToken ct = default) => Task.CompletedTask;
    }
}
