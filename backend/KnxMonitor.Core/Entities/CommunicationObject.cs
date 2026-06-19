namespace KnxMonitor.Core.Entities;

/// <summary>
/// A communication object on a device that links it to one or more group addresses.
/// One row per (device, group-address-link) pair so a GA -&gt; com-object lookup is a simple filter.
/// </summary>
public class CommunicationObject
{
    public int Id { get; set; }
    public int ProjectId { get; set; }

    /// <summary>Resolved physical address (a.l.d) of the owning device.</summary>
    public string DeviceAddress { get; set; } = string.Empty;

    public int Number { get; set; }
    public string? Name { get; set; }
    public string? FunctionText { get; set; }

    /// <summary>Resolved linked group address (m/m/s).</summary>
    public string GroupAddressLink { get; set; } = string.Empty;

    public string? DatapointType { get; set; }
    public string? Flags { get; set; }

    // Navigation properties
    public Project Project { get; set; } = null!;
}
