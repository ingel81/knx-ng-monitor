namespace KnxMonitor.Core.Entities;

/// <summary>
/// A node in the imported project's building/location hierarchy (Building, Floor, Room, ...).
/// The tree is stored flat; <see cref="ParentExternalId"/> references another row's <see cref="ExternalId"/>.
/// </summary>
public class Location
{
    public int Id { get; set; }
    public int ProjectId { get; set; }

    /// <summary>Internal ETS id of this node (e.g. "P-0600-0_BP-2").</summary>
    public string ExternalId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? ParentExternalId { get; set; }

    /// <summary>Comma-joined resolved physical addresses (a.l.d) of devices in this location.</summary>
    public string? DeviceAddresses { get; set; }

    /// <summary>Comma-joined resolved group addresses (m/m/s) referenced by this location.</summary>
    public string? GroupAddresses { get; set; }

    // Navigation properties
    public Project Project { get; set; } = null!;
}
