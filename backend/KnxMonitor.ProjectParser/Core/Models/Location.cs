namespace KnxMonitor.ProjectParser.Core.Models;

/// <summary>
/// A node in the project's building/location hierarchy.
/// ETS4 stores these as &lt;BuildingPart&gt; under &lt;Buildings&gt;, ETS5/6 as &lt;Space&gt; under &lt;Locations&gt;.
/// </summary>
public class Location
{
    public string Id { get; set; } = string.Empty;        // Internal ETS ID, e.g. "P-0600-0_BP-2"
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;       // Building, Floor, Room, Corridor, Stairway, ...
    public string? ParentId { get; set; }                  // null for the root node(s)

    /// <summary>Resolved physical addresses (a.l.d) of devices placed in this location.</summary>
    public List<string> DeviceRefIds { get; set; } = new();

    /// <summary>Resolved group addresses (m/m/s) referenced by this location.</summary>
    public List<string> GroupAddressRefIds { get; set; } = new();
}
