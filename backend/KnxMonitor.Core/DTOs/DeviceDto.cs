namespace KnxMonitor.Core.DTOs;

public class DeviceDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PhysicalAddress { get; set; } = string.Empty;
    public string? Manufacturer { get; set; }
    public string? ProductName { get; set; }
}
