namespace KnxMonitor.Core.DTOs;

/// <summary>One cold-archive day-file (NDJSON for today, gzipped for past days).</summary>
public class ArchiveDayDto
{
    public string Date { get; set; } = string.Empty;     // YYYY-MM-DD
    public string FileName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public bool Compressed { get; set; }
}
