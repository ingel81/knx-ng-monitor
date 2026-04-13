namespace KnxMonitor.ProjectParser.Core.Models;

public class GroupAddress
{
    public string Address { get; set; } = string.Empty;  // "1/2/3"
    public int RawAddress { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DptInfo? DatapointType { get; set; }
    public bool DataSecure { get; set; }
    public string? Identifier { get; set; }  // Internal ETS ID
}
