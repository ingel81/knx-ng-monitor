using KnxMonitor.Core.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KnxMonitor.Infrastructure.Services;

public class ImportJobCleanupService : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan JobMaxAge = TimeSpan.FromHours(1);

    private readonly IImportJobManager _jobManager;
    private readonly ILogger<ImportJobCleanupService> _logger;

    public ImportJobCleanupService(
        IImportJobManager jobManager,
        ILogger<ImportJobCleanupService> logger)
    {
        _jobManager = jobManager;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Import job cleanup worker started (max age {Hours}h, every {Minutes}min)",
            JobMaxAge.TotalHours, SweepInterval.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var removed = _jobManager.SweepCompleted(JobMaxAge);
                if (removed > 0)
                {
                    _logger.LogInformation("Swept {Count} stale import jobs", removed);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Import job sweep failed");
            }

            try
            {
                await Task.Delay(SweepInterval, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
        }
    }
}
