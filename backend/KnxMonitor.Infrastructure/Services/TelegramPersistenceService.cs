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

    private readonly Channel<KnxTelegram> _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TelegramPersistenceService> _logger;
    private readonly CancellationTokenSource _cts = new();
    private Task? _workerTask;

    public TelegramPersistenceService(
        IServiceScopeFactory scopeFactory,
        ILogger<TelegramPersistenceService> logger)
    {
        _scopeFactory = scopeFactory;
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
                    using var scope = _scopeFactory.CreateScope();
                    var repo = scope.ServiceProvider.GetRequiredService<ITelegramRepository>();
                    await repo.AddRangeAsync(batch);
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
