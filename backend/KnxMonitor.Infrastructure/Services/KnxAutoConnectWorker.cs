using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Interfaces;
using KnxMonitor.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KnxMonitor.Infrastructure.Services;

/// <summary>
/// Keeps the KNX bus link up without manual interaction. On startup — and whenever
/// the link drops (container restart, gateway reboot, network blip) — it (re)connects
/// to the configuration flagged <see cref="KnxConfiguration.AutoConnect"/>.
///
/// A user-initiated disconnect latches <see cref="IKnxConnectionService.ManualDisconnect"/>,
/// which this worker honours: it will not fight the user by reconnecting until the next
/// manual connect clears the latch.
/// </summary>
public class KnxAutoConnectWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(3);

    private readonly IKnxConnectionService _knx;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<KnxAutoConnectWorker> _logger;

    public KnxAutoConnectWorker(
        IKnxConnectionService knx,
        IServiceScopeFactory scopeFactory,
        ILogger<KnxAutoConnectWorker> logger)
    {
        _knx = knx;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let DB init + caches warm up before the first attempt.
        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_knx.IsConnected && !_knx.ManualDisconnect)
                {
                    var config = await GetAutoConnectConfigAsync(stoppingToken);
                    if (config != null)
                    {
                        _logger.LogInformation(
                            "Auto-connect: (re)connecting to KNX bus {IpAddress}:{Port}",
                            config.IpAddress, config.Port);
                        await _knx.ConnectAsync(config);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // Never let a transient failure kill the worker — just try again next tick.
                _logger.LogWarning(ex, "Auto-connect attempt failed; will retry");
            }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Picks the active auto-connect config, else the first auto-connect config.</summary>
    private async Task<KnxConfiguration?> GetAutoConnectConfigAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IKnxConfigurationRepository>();
        var configs = await repo.GetAllAsync();

        var candidates = configs.Where(c => c.AutoConnect).ToList();
        return candidates.FirstOrDefault(c => c.IsActive) ?? candidates.FirstOrDefault();
    }
}
