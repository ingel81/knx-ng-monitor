namespace KnxMonitor.ProjectParser.Core.Models;

public class ParseResult
{
    public List<GroupAddress> GroupAddresses { get; set; } = new();
    public List<Device> Devices { get; set; } = new();
    public ProjectFeatures Features { get; set; } = new();
    public ParseStatistics Statistics { get; set; } = new();
    public KeyringData? Keyring { get; set; }
}
