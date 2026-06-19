using KnxMonitor.Core.Entities;

namespace KnxMonitor.Core.Models;

/// <summary>
/// Mapped result of parsing a .knxproj: the Core entities ready to attach to a project.
/// </summary>
public class ParsedProjectData
{
    public List<GroupAddress> GroupAddresses { get; set; } = new();
    public List<Device> Devices { get; set; } = new();
    public List<Location> Locations { get; set; } = new();
    public List<CommunicationObject> CommunicationObjects { get; set; } = new();
    public List<GroupRange> GroupRanges { get; set; } = new();
    public List<ProjectKeyringKey> KeyringKeys { get; set; } = new();
}
