namespace KnxMonitor.Core.DTOs;

/// <summary>What a project deletion will affect — shown in the confirmation dialog.</summary>
public class ProjectDeletePreviewDto
{
    public int GroupAddressCount { get; set; }
    public int DeviceCount { get; set; }

    /// <summary>Recorded telegrams currently mapped to this project. They are kept on delete,
    /// but lose their group-address name mapping.</summary>
    public int TelegramCount { get; set; }
}
