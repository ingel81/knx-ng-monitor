namespace KnxMonitor.Core.DTOs;

public class ProjectDetailsDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTime ImportDate { get; set; }
    public bool IsActive { get; set; }
    public List<GroupAddressDto> GroupAddresses { get; set; } = new();
    public List<DeviceDto> Devices { get; set; } = new();
}
