using KnxMonitor.Core.Enums;

namespace KnxMonitor.Core.Entities;

public class KnxConfiguration
{
    public int Id { get; set; }
    public string IpAddress { get; set; } = "192.168.1.100";
    public int Port { get; set; } = 3671;
    public ConnectionType ConnectionType { get; set; } = ConnectionType.Tunneling;
    public string PhysicalAddress { get; set; } = "1.0.58";
    public bool IsActive { get; set; }

    /// <summary>
    /// When true, the backend connects to this configuration automatically on
    /// startup (and reconnects on link loss) without manual interaction.
    /// </summary>
    public bool AutoConnect { get; set; } = true;

    /// <summary>
    /// When true, the IP connection is established as a KNX IP Secure unicast tunnel
    /// (encrypted tunneling) instead of a plain tunnel. Default false — everyone today
    /// keeps the existing non-secure behavior. Secure credentials are derived best-effort
    /// from the active project's keyring blob; see <see cref="KnxConnectionService"/>.
    /// NOTE: the secure-tunnel path is unverified on real hardware.
    /// </summary>
    public bool UseSecureTunnel { get; set; } = false;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
