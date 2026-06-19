namespace KnxMonitor.Core.Entities;

/// <summary>
/// A main- or middle-group range of the imported project's group-address tree, carrying the
/// human-readable section name. A main range starts at <c>m*2048</c>, a middle range at
/// <c>m*2048 + middle*256</c>; the frontend matches a GA's group number against these to label
/// the tree's parent nodes.
/// </summary>
public class GroupRange
{
    public int Id { get; set; }
    public int ProjectId { get; set; }

    public string Name { get; set; } = string.Empty;
    public int RangeStart { get; set; }
    public int RangeEnd { get; set; }

    // Navigation properties
    public Project Project { get; set; } = null!;
}
