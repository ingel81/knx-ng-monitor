using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Interfaces;
using KnxMonitor.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KnxMonitor.Infrastructure.Services;

/// <summary>
/// Writes one <see cref="MonitorHeartbeat"/> per minute so gaps in the telegram history can be
/// explained afterwards. See <see cref="AvailabilityCalculator"/> for how the beats are read back.
/// </summary>
/// <remarks>
/// One insert per minute is nothing next to the telegram write load, and the table stays small
/// (1440 rows/day, trimmed to <see cref="RetentionDays"/>). The first beat is written immediately
/// at startup rather than after the first tick, so a restart boundary is pinned to within seconds
/// instead of a whole interval.
/// </remarks>
public class MonitorHeartbeatWorker : BackgroundService
{
    /// <summary>Beat cadence. <see cref="AvailabilityCalculator"/> judges gaps against a multiple of this.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    /// <summary>How far back beats are kept. Older ranges report as Unknown, not as an outage.</summary>
    public const int RetentionDays = 90;
    private static readonly TimeSpan RetentionInterval = TimeSpan.FromHours(6);

    private readonly IKnxConnectionService _knx;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MonitorHeartbeatWorker> _logger;

    private int _telegramsSinceLastBeat;
    private DateTime _lastRetention = DateTime.MinValue;

    public MonitorHeartbeatWorker(
        IKnxConnectionService knx,
        IServiceScopeFactory scopeFactory,
        ILogger<MonitorHeartbeatWorker> logger)
    {
        _knx = knx;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _knx.TelegramReceived += OnTelegramReceived;
        _logger.LogInformation("Monitor heartbeat worker started ({Seconds}s interval)", Interval.TotalSeconds);

        try
        {
            // Pin the startup boundary right away instead of after a full interval.
            await WriteBeatAsync(stoppingToken);

            using var timer = new PeriodicTimer(Interval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await WriteBeatAsync(stoppingToken);
                await MaybeRunRetentionAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
        finally
        {
            _knx.TelegramReceived -= OnTelegramReceived;

            // A final beat on a clean shutdown narrows the boundary. Best effort only — the cases
            // this feature exists for (power loss, standby, kill) never reach this line.
            try { await WriteBeatAsync(CancellationToken.None); }
            catch (Exception ex) { _logger.LogDebug(ex, "Final heartbeat could not be written"); }

            _logger.LogInformation("Monitor heartbeat worker stopped");
        }
    }

    private void OnTelegramReceived(object? sender, KnxTelegram telegram) =>
        Interlocked.Increment(ref _telegramsSinceLastBeat);

    private async Task WriteBeatAsync(CancellationToken ct)
    {
        // Read-and-reset in one step so telegrams arriving during the write land in the next beat
        // instead of being counted twice or lost.
        var count = Interlocked.Exchange(ref _telegramsSinceLastBeat, 0);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IMonitorHeartbeatRepository>();
            await repo.AddAsync(new MonitorHeartbeat
            {
                Timestamp = DateTime.UtcNow,
                State = _knx.State,
                TelegramsSinceLast = count
            });
        }
        catch (Exception ex)
        {
            // Give the count back so a transient DB failure does not silently swallow it.
            Interlocked.Add(ref _telegramsSinceLastBeat, count);
            _logger.LogWarning(ex, "Failed to write monitor heartbeat");
        }
    }

    private async Task MaybeRunRetentionAsync(CancellationToken ct)
    {
        if (DateTime.UtcNow - _lastRetention < RetentionInterval)
        {
            return;
        }
        _lastRetention = DateTime.UtcNow;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IMonitorHeartbeatRepository>();
            var removed = await repo.DeleteOlderThanAsync(DateTime.UtcNow.AddDays(-RetentionDays), ct);
            if (removed > 0)
            {
                _logger.LogInformation("Heartbeat retention removed {Count} beats older than {Days} days",
                    removed, RetentionDays);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Heartbeat retention pass failed");
        }
    }
}
