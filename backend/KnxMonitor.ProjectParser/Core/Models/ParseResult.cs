namespace KnxMonitor.ProjectParser.Core.Models;

public class ParseResult
{
    public List<GroupAddress> GroupAddresses { get; set; } = new();
    public List<Device> Devices { get; set; } = new();
    public List<Location> Locations { get; set; } = new();
    public List<ComObject> CommunicationObjects { get; set; } = new();
    public List<GroupRange> GroupRanges { get; set; } = new();
    public ProjectFeatures Features { get; set; } = new();
    public ParseStatistics Statistics { get; set; } = new();
    public KeyringData? Keyring { get; set; }
}
