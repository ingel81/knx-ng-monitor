namespace KnxMonitor.ProjectParser.Core.Models;

/// <summary>
/// A communication object instance on a device, linking the device to one or more group addresses.
/// Parsed from &lt;ComObjectInstanceRef&gt; either via &lt;Connectors&gt;/&lt;Send&gt;/&lt;Receive&gt; (ETS4)
/// or the @Links attribute (ETS5/6).
/// </summary>
public class ComObject
{
    /// <summary>Resolved physical address (a.l.d) of the owning device.</summary>
    public string DeviceAddress { get; set; } = string.Empty;

    /// <summary>Communication object number (extracted from the O-&lt;n&gt; part of the RefId; 0 if unknown).</summary>
    public int Number { get; set; }

    public string? Name { get; set; }
    public string? FunctionText { get; set; }

    /// <summary>Resolved group addresses (m/m/s) linked to this com object.</summary>
    public List<string> GroupAddressLinks { get; set; } = new();

    public string? DatapointType { get; set; }

    /// <summary>Send/Receive direction or flag summary (e.g. "Send", "Receive", "Send,Receive").</summary>
    public string? Flags { get; set; }
}
