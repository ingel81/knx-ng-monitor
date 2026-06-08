using System.Diagnostics;
using System.Threading.Channels;
using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KnxMonitor.Infrastructure.Services;

public class TelegramPersistenceService : ITelegramQueue, IHostedService, IAsyncDisposable
{
    private const int Capacity = 10_000;
    private const int BatchSize = 100;

    // Retention cadence — run at most ~every 10s OR every N batches, whichever comes first.
    private const int RetentionEveryBatches = 20;
    private static readonly TimeSpan RetentionInterval = TimeSpan.FromSeconds(10);

    // Chunked delete keeps each write transaction short; cap chunks per pass so a large
    // trim (e.g. after lowering the limit) never starves the channel drain.
    private const int DeleteChunkSize = 5_000;
    private const int MaxDeleteChunksPerPass = 20;

    private readonly Channel<KnxTelegram> _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRecordingSettingsProvider _settingsProvider;
    private readonly ILogger<TelegramPersistenceService> _logger;
    private readonly CancellationTokenSource _cts = new();
    private Task? _workerTask;

    private int _batchesSinceRetention;
    private DateTime _lastRetention = DateTime.UtcNow;

    public TelegramPersistenceService(
        IServiceScopeFactory scopeFactory,
        IRecordingSettingsProvider settingsProvider,
        ILogger<TelegramPersistenceService> logger)
    {
        _scopeFactory = scopeFactory;
        _settingsProvider = settingsProvider;
        _logger = logger;
        _channel = Channel.CreateBounded<KnxTelegram>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public bool TryEnqueue(KnxTelegram telegram) => _channel.Writer.TryWrite(telegram);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _workerTask = Task.Run(() => RunAsync(_cts.Token));
        _logger.LogInformation("Telegram persistence worker started");
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _channel.Writer.TryComplete();
        _cts.Cancel();

        if (_workerTask != null)
        {
            try
            {
                await _workerTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) { }
        }
        _logger.LogInformation("Telegram persistence worker stopped");
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var batch = new List<KnxTelegram>(BatchSize);
        try
        {
            await foreach (var first in _channel.Reader.ReadAllAsync(ct))
            {
                batch.Add(first);

                // Drain anything immediately available so we batch under bursts.
                while (batch.Count < BatchSize && _channel.Reader.TryRead(out var more))
                {
                    batch.Add(more);
                }

                try
                {
                    // Single scope/connection per batch — retention runs on the SAME
                    // connection right after the insert, so there is only ever one SQLite writer.
                    using var scope = _scopeFactory.CreateScope();
                    var repo = scope.ServiceProvider.GetRequiredService<ITelegramRepository>();
                    await repo.AddRangeAsync(batch);

                    _batchesSinceRetention++;
                    if (_batchesSinceRetention >= RetentionEveryBatches ||
                        DateTime.UtcNow - _lastRetention >= RetentionInterval)
                    {
                        await RunRetentionAsync(repo, ct);
                        _batchesSinceRetention = 0;
                        _lastRetention = DateTime.UtcNow;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to persist batch of {Count} telegrams", batch.Count);
                }

                batch.Clear();
            }
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
    }

    /// <summary>
    /// Trims the count-based ring buffer to the live HotBufferMaxCount, then truncates the WAL.
    /// Runs on the worker's batch scope (single writer); failures are logged, never fatal.
    /// </summary>
    private async Task RunRetentionAsync(ITelegramRepository repo, CancellationToken ct)
    {
        try
        {
            var maxCount = _settingsProvider.HotBufferMaxCount;
            var maxId = await repo.GetMaxIdAsync();

            if (maxId > 0 && maxCount > 0)
            {
                var thresholdId = maxId - maxCount;
                if (thresholdId > 0)
                {
                    var chunks = 0;
                    int deleted;
                    do
                    {
                        deleted = await repo.DeleteOlderThanIdChunkAsync(thresholdId, DeleteChunkSize);
                        chunks++;
                    }
                    while (deleted == DeleteChunkSize && chunks < MaxDeleteChunksPerPass && !ct.IsCancellationRequested);
                }
            }

            await repo.CheckpointWalAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram retention pass failed");
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _channel.Writer.TryComplete();
        if (_workerTask != null)
        {
            try { await _workerTask; } catch { /* ignore */ }
        }
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }
}
