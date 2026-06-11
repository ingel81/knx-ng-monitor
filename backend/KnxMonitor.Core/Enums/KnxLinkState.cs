namespace KnxMonitor.Core.Enums;

/// <summary>Lifecycle state of the KNX bus link (drives the UI status indicator).</summary>
public enum KnxLinkState
{
    Disconnected,
    Connecting,
    Connected
}
