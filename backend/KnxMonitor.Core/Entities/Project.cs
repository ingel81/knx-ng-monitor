namespace KnxMonitor.Core.Entities;

public class Project
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTime ImportDate { get; set; }
    public string? ProjectData { get; set; }

    // Navigation properties
    public ICollection<GroupAddress> GroupAddresses { get; set; } = new List<GroupAddress>();
    public ICollection<Device> Devices { get; set; } = new List<Device>();
}
