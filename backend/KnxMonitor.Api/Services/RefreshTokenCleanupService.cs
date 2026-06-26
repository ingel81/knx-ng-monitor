using KnxMonitor.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KnxMonitor.Api.Services;

/// <summary>
/// Periodically prunes the RefreshTokens table: removes expired tokens and revoked tokens
/// older than a grace window, so the table does not grow unbounded.
/// </summary>
public class RefreshTokenCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RefreshTokenCleanupService> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private static readonly TimeSpan RevokedGrace = TimeSpan.FromDays(7);

    public RefreshTokenCleanupService(IServiceScopeFactory scopeFactory, ILogger<RefreshTokenCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var now = DateTime.UtcNow;
                var revokedCutoff = now - RevokedGrace;

                var removed = await db.RefreshTokens
                    .Where(rt => rt.ExpiresAt < now || (rt.IsRevoked && rt.CreatedAt < revokedCutoff))
                    .ExecuteDeleteAsync(stoppingToken);

                if (removed > 0)
                {
                    _logger.LogInformation("Pruned {Count} stale refresh tokens", removed);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Refresh token cleanup failed");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
