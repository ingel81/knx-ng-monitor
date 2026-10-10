namespace KnxMonitor.ProjectParser.Core.Models;

/// <summary>
/// An ETS function ("Licht Wohnzimmer", "Heizung Bad"): a named bundle of group addresses placed
/// in a space. ETS 5/6 store these as &lt;Function&gt; elements inside a &lt;Space&gt;, each linking
/// its group addresses through &lt;GroupAddressRef RefId="..."&gt;. ETS 4 has no functions.
/// </summary>
public class ProjectFunction
{
    public string Id { get; set; } = string.Empty;          // Internal ETS ID, e.g. "P-05C0-0_F-1"
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;        // Function type, e.g. "FT-1" (switchable light)

    /// <summary>Id of the space the function sits in; null when it is not inside one.</summary>
    public string? SpaceId { get; set; }

    /// <summary>Resolved group addresses (m/m/s) of the function, in project order.</summary>
    public List<string> GroupAddresses { get; set; } = new();
}
