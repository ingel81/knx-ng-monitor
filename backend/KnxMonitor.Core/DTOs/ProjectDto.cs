namespace KnxMonitor.Core.DTOs;

public class ProjectDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTime ImportDate { get; set; }
    public bool IsActive { get; set; }
    public int GroupAddressCount { get; set; }
    public int DeviceCount { get; set; }
}
