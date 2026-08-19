using KnxMonitor.Core.Enums;

namespace KnxMonitor.Core.Entities;

/// <summary>
/// One tick of the liveness record, written every minute while the process runs.
/// </summary>
/// <remarks>
/// Exists to disambiguate holes in <see cref="KnxTelegram"/>: an empty stretch of history can mean
/// a quiet bus, a lost bus link, or a monitor that simply was not running, and the telegram table
/// alone cannot tell those apart. A heartbeat with <see cref="KnxLinkState.Connected"/> proves the
/// stretch was genuinely quiet; one with any other state proves the link was down; a missing
/// heartbeat proves the process was not running. Deliberately does not rely on a shutdown marker,
/// because the interesting cases (power loss, standby, kill) never get to write one.
/// </remarks>
public class MonitorHeartbeat
{
    public int Id { get; set; }

    /// <summary>UTC instant the beat was written.</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>Bus link state at that instant.</summary>
    public KnxLinkState State { get; set; }

    /// <summary>Telegrams received since the previous beat (0 = bus was quiet, not a data loss).</summary>
    public int TelegramsSinceLast { get; set; }
}
