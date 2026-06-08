using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KnxMonitor.Infrastructure.Services;

/// <summary>
/// Caches the single <see cref="RecordingSettings"/> row as an atomically-swappable snapshot.
/// The persistence worker and the archive service read this on their hot paths without DB I/O.
/// </summary>
public class RecordingSettingsProvider : IRecordingSettingsProvider
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RecordingSettingsProvider> _logger;

    // Reference-swap is atomic; volatile guarantees readers see the latest snapshot.
    private volatile RecordingSettings _current = new();

    public RecordingSettingsProvider(
        IServiceScopeFactory scopeFactory,
        ILogger<RecordingSettingsProvider> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public RecordingSettings Current => _current;
    public int HotBufferMaxCount => _current.HotBufferMaxCount;
    public bool ArchiveEnabled => _current.ArchiveEnabled;
    public int? ArchiveRetentionDays => _current.ArchiveRetentionDays;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRecordingSettingsRepository>();
        var settings = await repo.GetOrCreateAsync();
        _current = Snapshot(settings);
        _logger.LogInformation(
            "Recording settings loaded: HotBufferMaxCount={Count}, ArchiveEnabled={Archive}, ArchiveRetentionDays={Retention}",
            _current.HotBufferMaxCount, _current.ArchiveEnabled, _current.ArchiveRetentionDays);
    }

    public async Task UpdateAsync(RecordingSettings updated, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRecordingSettingsRepository>();

        var entity = await repo.GetOrCreateAsync();
        entity.HotBufferMaxCount = updated.HotBufferMaxCount;
        entity.ArchiveEnabled = updated.ArchiveEnabled;
        entity.ArchiveRetentionDays = updated.ArchiveRetentionDays;
        entity.UpdatedAt = DateTime.UtcNow;

        await repo.UpdateAsync(entity);

        // Atomic swap of an isolated snapshot (callers must not mutate the shared instance).
        _current = Snapshot(entity);
        _logger.LogInformation(
            "Recording settings updated: HotBufferMaxCount={Count}, ArchiveEnabled={Archive}, ArchiveRetentionDays={Retention}",
            _current.HotBufferMaxCount, _current.ArchiveEnabled, _current.ArchiveRetentionDays);
    }

    private static RecordingSettings Snapshot(RecordingSettings source) => new()
    {
        Id = source.Id,
        HotBufferMaxCount = source.HotBufferMaxCount,
        ArchiveEnabled = source.ArchiveEnabled,
        ArchiveRetentionDays = source.ArchiveRetentionDays,
        UpdatedAt = source.UpdatedAt
    };
}
