namespace KnxMonitor.Core.Enums;

/// <summary>How complete the recorded history is over a given stretch of time.</summary>
public enum AvailabilityState
{
    /// <summary>Monitor ran and the bus link was up — an empty stretch here means a quiet bus.</summary>
    Up,

    /// <summary>Monitor ran but the bus link was down — telegrams in this stretch were missed.</summary>
    BusDown,

    /// <summary>No heartbeat at all — the process was not running (restart, standby, crash).</summary>
    MonitorDown,

    /// <summary>
    /// Before the liveness record begins (or past its retention): nothing is known about this
    /// stretch. Distinct from <see cref="MonitorDown"/> on purpose — after an upgrade the whole
    /// pre-upgrade history has no beats, and calling that an outage would assert the opposite of
    /// the truth about telegrams that were in fact recorded.
    /// </summary>
    Unknown
}
