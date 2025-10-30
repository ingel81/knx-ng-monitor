using KnxMonitor.Core.Enums;

namespace KnxMonitor.Core.DTOs;

public class ImportStepDto
{
    public ImportStepType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "pending"; // pending, in-progress, completed, failed
    public int Progress { get; set; } // 0-100
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string? ErrorMessage { get; set; }
}
