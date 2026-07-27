namespace KnxMonitor.Core.Enums;

/// <summary>
/// Result of a connection probe. Routing needs a third state: it is connectionless, so joining
/// the multicast group succeeds locally even when no packet ever arrives — a plain boolean would
/// report a green "connected" for a setup that will never see a single telegram.
/// </summary>
public enum ConnectionTestOutcome
{
    /// <summary>Reachable — tunnel established, or routing traffic actually observed.</summary>
    Success,

    /// <summary>Could not connect at all.</summary>
    Failed,

    /// <summary>
    /// Routing only: the multicast group was joined, but no bus traffic arrived within the
    /// listening window. Either the bus is idle, or multicast never reaches this process
    /// (different VLAN, container without host networking, multicast not routed).
    /// </summary>
    JoinedWithoutTraffic
}
