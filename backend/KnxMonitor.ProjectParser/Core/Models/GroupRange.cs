namespace KnxMonitor.ProjectParser.Core.Models;

/// <summary>
/// A main- or middle-group range in the project's group-address tree. ETS stores these as nested
/// &lt;GroupRange&gt; elements (a main group's GroupRange contains the middle groups' GroupRanges),
/// giving the otherwise number-only GA tree its human-readable section names.
/// A main range starts at <c>m*2048</c>, a middle range at <c>m*2048 + middle*256</c>.
/// </summary>
public class GroupRange
{
    public string Name { get; set; } = string.Empty;
    public int RangeStart { get; set; }
    public int RangeEnd { get; set; }
}
