namespace KnxMonitor.Core.DTOs;

public class RecordingSettingsDto
{
    public int HotBufferMaxCount { get; set; }
    public bool ArchiveEnabled { get; set; }
    public int? ArchiveRetentionDays { get; set; }
}
