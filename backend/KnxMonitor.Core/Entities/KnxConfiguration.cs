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

    /// <summary>Tunnel transport. UDP by default; TCP is opt-in (see <see cref="Enums.TunnelProtocol"/>).</summary>
    public TunnelProtocol TunnelProtocol { get; set; } = TunnelProtocol.Udp;

    /// <summary>
    /// Individual address to request for the tunnel (KNXnet/IP Tunnelling v2), e.g. "1.1.251".
    /// Null or empty lets the interface pick a free slot, which is the behavior so far. A fixed
    /// address stops every reconnect from occupying yet another slot while the old one times out.
    /// </summary>
    public string? TunnelAddress { get; set; }

    /// <summary>
    /// When the requested <see cref="TunnelAddress"/> is taken, accept any free one instead of
    /// failing the connect. Default true, so a fixed address can never lock the monitor out.
    /// </summary>
    public bool TunnelAddressFallback { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
