namespace KnxMonitor.Core.DTOs;

public class CommunicationObjectDto
{
    public int Id { get; set; }
    public string DeviceAddress { get; set; } = string.Empty;
    public string? DeviceName { get; set; }
    public string? DeviceManufacturer { get; set; }
    public string? DeviceProductName { get; set; }
    public int Number { get; set; }
    public string? Name { get; set; }
    public string? FunctionText { get; set; }
    public string GroupAddressLink { get; set; } = string.Empty;
    public string? DatapointType { get; set; }
    public string? Flags { get; set; }
}
