namespace KnxMonitor.Core.DTOs;

/// <summary>
/// A single log line surfaced to the in-app log viewer / diagnostics download.
/// </summary>
public class LogEntryDto
{
    public DateTimeOffset Timestamp { get; set; }
    public string Level { get; set; } = "Information";
    public string Message { get; set; } = string.Empty;
    public string? Source { get; set; }
    public string? Exception { get; set; }
}
