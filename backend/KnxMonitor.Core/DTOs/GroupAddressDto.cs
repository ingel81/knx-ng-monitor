namespace KnxMonitor.Core.DTOs;

public class GroupAddressDto
{
    public int Id { get; set; }
    public string Address { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? DatapointType { get; set; }
}
