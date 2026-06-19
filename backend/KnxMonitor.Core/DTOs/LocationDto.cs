namespace KnxMonitor.Core.DTOs;

/// <summary>
/// Flat node of the building/location tree. The frontend rebuilds the tree from
/// <see cref="ExternalId"/> / <see cref="ParentExternalId"/>.
/// </summary>
public class LocationDto
{
    public int Id { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? ParentExternalId { get; set; }
    public List<string> DeviceAddresses { get; set; } = new();
    public List<string> GroupAddresses { get; set; } = new();
}
