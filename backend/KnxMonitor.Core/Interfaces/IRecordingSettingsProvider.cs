using KnxMonitor.Core.Entities;

namespace KnxMonitor.Core.Interfaces;

/// <summary>
/// Singleton, thread-safe, cached provider for the live recording settings.
/// Reads are hot-path safe (no DB round-trip per telegram / per retention pass).
/// </summary>
public interface IRecordingSettingsProvider
{
    /// <summary>Latest cached snapshot. Never null after <see cref="InitializeAsync"/>.</summary>
    RecordingSettings Current { get; }

    int HotBufferMaxCount { get; }
    bool ArchiveEnabled { get; }
    int? ArchiveRetentionDays { get; }

    /// <summary>Loads the initial snapshot from the database (call once at startup).</summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>Persists the new settings and atomically swaps the cached snapshot (live-apply, no restart).</summary>
    Task UpdateAsync(RecordingSettings updated, CancellationToken ct = default);
}
