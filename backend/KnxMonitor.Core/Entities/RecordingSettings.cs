namespace KnxMonitor.Core.Entities;

/// <summary>
/// Single-row configuration for the recording feature (hot-tier ring buffer + cold archive).
/// </summary>
public class RecordingSettings
{
    public int Id { get; set; }

    /// <summary>Count-based hot-tier ring buffer size. The persistence worker trims rows beyond this.</summary>
    public int HotBufferMaxCount { get; set; } = 1_000_000;

    /// <summary>When true, every telegram is also written to the NDJSON+gzip cold archive.</summary>
    public bool ArchiveEnabled { get; set; } = false;

    /// <summary>Delete archive day-files older than this many days. Null = keep forever.</summary>
    public int? ArchiveRetentionDays { get; set; } = null;

    public DateTime UpdatedAt { get; set; }
}
