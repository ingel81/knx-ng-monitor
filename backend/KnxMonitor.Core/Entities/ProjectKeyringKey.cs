namespace KnxMonitor.Core.Entities;

/// <summary>
/// Decrypted keyring material stored against a project for later KNX Secure runtime use.
/// One row per key (per-GA key, per-device tool key, or the single backbone key).
/// </summary>
public class ProjectKeyringKey
{
    public int Id { get; set; }
    public int ProjectId { get; set; }

    /// <summary>"GroupAddress", "ToolKey" or "Backbone".</summary>
    public string KeyType { get; set; } = string.Empty;

    /// <summary>Group address (m/m/s) for GroupAddress keys; null otherwise.</summary>
    public string? GroupAddress { get; set; }

    /// <summary>Individual/physical address (a.l.d) for ToolKey entries; null otherwise.</summary>
    public string? IndividualAddress { get; set; }

    /// <summary>Base64-encoded key material.</summary>
    public string KeyBase64 { get; set; } = string.Empty;

    // Navigation properties
    public Project Project { get; set; } = null!;
}
