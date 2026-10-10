namespace KnxMonitor.Core.Enums;

/// <summary>
/// Transport of a KNXnet/IP tunnel. UDP is what every interface speaks; TCP (KNXnet/IP
/// Core v2) retransmits lost packets and survives a lossy network path far better, but
/// only newer interfaces support it.
/// </summary>
public enum TunnelProtocol
{
    Udp,
    Tcp
}
