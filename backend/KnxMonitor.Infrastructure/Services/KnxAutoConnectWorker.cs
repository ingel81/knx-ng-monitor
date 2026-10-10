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
///
/// Reconnects are event-driven (#22): <see cref="IKnxConnectionService.LinkLost"/> wakes the
/// worker immediately, failed attempts back off from 1 s up to the poll interval, and the poll
/// stays as a safety net. While Falcon's own AutoReconnect is still working on a dropped tunnel
/// (<see cref="IKnxConnectionService.IsRecovering"/>) the worker keeps out of its way.
/// </summary>
public class KnxAutoConnectWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(1);

    // A link has to hold this long before the backoff starts over. Otherwise a link that drops
    // right after every connect would be retried once a second forever.
    private static readonly TimeSpan StableLinkTime = TimeSpan.FromSeconds(30);

    // Released by LinkLost; the loop waits on it instead of a plain delay.
    private readonly SemaphoreSlim _wake = new(0, 1);

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
        _knx.LinkLost += OnLinkLost;
        try
        {
            // Let DB init + caches warm up before the first attempt.
            try { await Task.Delay(StartupDelay, stoppingToken); }
            catch (OperationCanceledException) { return; }

            var failures = 0;
            var connectedAt = DateTime.MinValue;
            while (!stoppingToken.IsCancellationRequested)
            {
                var attempted = false;
                var succeeded = false;
                try
                {
                    // Skip while a test probe is running (it pauses and restores the live link
                    // itself) and while Falcon is still recovering the tunnel on its own; either way
                    // reconnecting here would open a competing second tunnel.
                    if (!_knx.IsConnected && !_knx.ManualDisconnect && !_knx.TestInProgress && !_knx.IsRecovering)
                    {
                        var config = await GetAutoConnectConfigAsync(stoppingToken);
                        if (config != null)
                        {
                            _logger.LogInformation(
                                "Auto-connect: (re)connecting to KNX bus {IpAddress}:{Port}",
                                config.IpAddress, config.Port);
                            attempted = true;
                            succeeded = await _knx.ConnectAsync(config);
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

                if (attempted && succeeded)
                {
                    // Counted as a failure until the link has proven stable (see StableLinkTime).
                    connectedAt = DateTime.UtcNow;
                    failures++;
                }
                else if (attempted)
                {
                    failures++;
                }
                else if (_knx.IsConnected && DateTime.UtcNow - connectedAt >= StableLinkTime)
                {
                    failures = 0;
                }

                var delay = attempted && succeeded ? PollInterval : NextDelay(failures, _knx.IsRecovering);
                var settle = failures == 0 ? FirstRetryDelay : NextDelay(failures, false);
                try { await WaitForNextAttemptAsync(delay, settle, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
        finally
        {
            _knx.LinkLost -= OnLinkLost;
        }
    }

    /// <summary>
    /// Delay before the next check: 1 s, 2 s, 4 s, 8 s after consecutive failed attempts, capped at
    /// the poll interval. While Falcon recovers the tunnel, check again shortly so the worker takes
    /// over as soon as the grace period runs out.
    /// </summary>
    internal static TimeSpan NextDelay(int consecutiveFailures, bool recovering)
    {
        if (recovering)
        {
            return FirstRetryDelay;
        }
        if (consecutiveFailures <= 0)
        {
            return PollInterval;
        }

        var backoff = FirstRetryDelay * Math.Pow(2, Math.Min(consecutiveFailures - 1, 4));
        return backoff < PollInterval ? backoff : PollInterval;
    }

    private async Task WaitForNextAttemptAsync(TimeSpan delay, TimeSpan settleAfterWake, CancellationToken ct)
    {
        // Returns early when LinkLost fires; then give the transport a moment to settle before
        // the reconnect (1 s, growing with the backoff when the link keeps dropping), otherwise the
        // first attempt often hits the same outage.
        if (await _wake.WaitAsync(delay, ct))
        {
            await Task.Delay(settleAfterWake < FirstRetryDelay ? FirstRetryDelay : settleAfterWake, ct);
        }
    }

    private void OnLinkLost(object? sender, EventArgs e)
    {
        // Capacity 1: several drops before the loop wakes still mean one reconnect.
        try { _wake.Release(); }
        catch (SemaphoreFullException) { }
    }

    public override void Dispose()
    {
        _wake.Dispose();
        base.Dispose();
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
