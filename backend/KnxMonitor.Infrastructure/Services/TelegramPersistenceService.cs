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

    // With a write interval (#37) one flush covers the whole interval. Capped so a busy bus
    // cannot grow the buffer without bound; at the cap it is written early.
    private const int MaxBufferedTelegrams = 20_000;

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

    private long _droppedTotal;
    private long _droppedLogged;

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
            // Wait-mode + TryWrite is what makes an overflow observable: TryWrite returns false
            // instead of blocking, and the drop gets counted. DropOldest would make TryWrite always
            // return true and lose telegrams without a trace.
            //
            // Deliberate trade-off: under a sustained burst the queue keeps its oldest entries and
            // the newest telegrams are the ones dropped, so a hole lands at the end of the burst
            // rather than at its start. Counted-and-reported beats silently-correct here — a hole
            // nobody knows about is worse than a hole at a known position.
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <summary>Telegrams dropped because the queue was full, since process start.</summary>
    public long DroppedCount => Interlocked.Read(ref _droppedTotal);

    public bool TryEnqueue(KnxTelegram telegram)
    {
        if (_channel.Writer.TryWrite(telegram))
        {
            return true;
        }

        // Counted here and reported cumulatively by the worker: logging per telegram would spam
        // the log with one line per lost telegram exactly when the system is already struggling.
        Interlocked.Increment(ref _droppedTotal);
        return false;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _workerTask = Task.Run(() => RunAsync(_cts.Token));
        _logger.LogInformation("Telegram persistence worker started");
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Complete the queue and let the worker drain and write what is buffered. Cancelling
        // first would drop it, which with a write interval can be minutes of telegrams. Only when
        // the host's shutdown timeout runs out is the worker cancelled.
        _channel.Writer.TryComplete();

        if (_workerTask != null)
        {
            try
            {
                await _workerTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _cts.Cancel();
            }
        }
        _logger.LogInformation("Telegram persistence worker stopped");
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var batch = new List<KnxTelegram>(BatchSize);
        var reader = _channel.Reader;
        try
        {
            // Ends once the queue is completed (shutdown) and fully drained.
            while (await reader.WaitToReadAsync(ct))
            {
                var interval = TimeSpan.FromSeconds(Math.Max(0, _settingsProvider.HistoryWriteIntervalSeconds));
                if (interval == TimeSpan.Zero)
                {
                    // Immediate mode: write what is there right now, batching only under bursts.
                    while (batch.Count < BatchSize && reader.TryRead(out var item))
                    {
                        batch.Add(item);
                    }
                }
                else
                {
                    await CollectForIntervalAsync(batch, interval, ct);
                }

                if (batch.Count == 0)
                {
                    continue;
                }

                await PersistAsync(batch, retentionDue: interval > TimeSpan.Zero, ct);
                batch.Clear();
                LogDropsIfAny();
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown timeout ran out; whatever is still buffered is lost.
            if (batch.Count > 0)
            {
                _logger.LogWarning("Shutdown cut the history write short; {Count} buffered telegrams were not written", batch.Count);
            }
        }
    }

    /// <summary>
    /// Buffered mode (#37): keeps reading until the interval since the first telegram has passed,
    /// the buffer reaches its cap, or the queue is completed. Returns early on shutdown so the
    /// final write happens right away instead of after the interval.
    /// </summary>
    private async Task CollectForIntervalAsync(List<KnxTelegram> batch, TimeSpan interval, CancellationToken ct)
    {
        var reader = _channel.Reader;
        var deadline = DateTime.UtcNow + interval;

        while (batch.Count < MaxBufferedTelegrams)
        {
            while (batch.Count < MaxBufferedTelegrams && reader.TryRead(out var item))
            {
                batch.Add(item);
            }

            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero || batch.Count >= MaxBufferedTelegrams)
            {
                return;
            }

            using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
            wait.CancelAfter(remaining);
            try
            {
                if (!await reader.WaitToReadAsync(wait.Token))
                {
                    return; // queue completed: shutdown, write now
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return; // interval over
            }
        }
    }

    private async Task PersistAsync(List<KnxTelegram> batch, bool retentionDue, CancellationToken ct)
    {
        try
        {
            // Single scope/connection per batch — retention runs on the SAME
            // connection right after the insert, so there is only ever one SQLite writer.
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<ITelegramRepository>();
            await repo.AddRangeAsync(batch);

            // In buffered mode every flush also trims and checkpoints: the WAL is synced to the
            // database file once per interval instead of every ten seconds.
            _batchesSinceRetention++;
            if (retentionDue ||
                _batchesSinceRetention >= RetentionEveryBatches ||
                DateTime.UtcNow - _lastRetention >= RetentionInterval)
            {
                await RunRetentionAsync(repo, ct);
                _batchesSinceRetention = 0;
                _lastRetention = DateTime.UtcNow;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to persist batch of {Count} telegrams", batch.Count);
        }
    }

    private void LogDropsIfAny()
    {
        var total = Interlocked.Read(ref _droppedTotal);
        if (total > _droppedLogged)
        {
            _logger.LogWarning(
                "Telegram persistence queue overflow: {Count} telegrams dropped (cumulative)", total);
            _droppedLogged = total;
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
