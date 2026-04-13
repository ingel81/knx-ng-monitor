namespace KnxMonitor.ProjectParser.Core.Models;

public class Device
{
    public string PhysicalAddress { get; set; } = string.Empty;  // "1.1.1"
    public int RawAddress { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Manufacturer { get; set; }
    public string? ProductName { get; set; }
    public string? ProductRefId { get; set; }
    public string? HardwareProgramRefId { get; set; }
    public string? ApplicationProgramRefId { get; set; }
    public bool HasKnxSecure { get; set; }
}
