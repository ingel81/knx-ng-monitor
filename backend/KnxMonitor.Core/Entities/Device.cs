namespace KnxMonitor.Core.Entities;

public class Device
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PhysicalAddress { get; set; } = string.Empty;
    public string? Manufacturer { get; set; }
    public string? ProductName { get; set; }

    // Navigation properties
    public Project Project { get; set; } = null!;
}
