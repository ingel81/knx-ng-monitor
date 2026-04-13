namespace KnxMonitor.ProjectParser.Core.Models;

public class ParseStatistics
{
    public TimeSpan Duration { get; set; }
    public int GroupAddressCount { get; set; }
    public int DeviceCount { get; set; }
    public List<string> Warnings { get; set; } = new();
}
