namespace KnxMonitor.Core.DTOs;

/// <summary>
/// A main- or middle-group range with its section name. The frontend matches a group address's
/// numeric components against these ranges to label the GA tree's main/middle nodes
/// (main range: RangeStart = m*2048; middle range: RangeStart = m*2048 + middle*256).
/// </summary>
public class GroupRangeDto
{
    public string Name { get; set; } = string.Empty;
    public int RangeStart { get; set; }
    public int RangeEnd { get; set; }
}
