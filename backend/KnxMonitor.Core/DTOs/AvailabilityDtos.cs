using KnxMonitor.Core.Enums;

namespace KnxMonitor.Core.DTOs;

/// <summary>One stretch of time with a uniform <see cref="AvailabilityState"/>.</summary>
public class AvailabilityIntervalDto
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public AvailabilityState State { get; set; }

    /// <summary>Length in minutes, rounded to one decimal.</summary>
    public double Minutes { get; set; }

    /// <summary>Telegrams recorded inside this interval (always 0 while down).</summary>
    public long Telegrams { get; set; }
}

/// <summary>Availability of the recording over a queried range.</summary>
public class AvailabilityResponse
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }

    /// <summary>Beat cadence the intervals were derived from; a missing beat is judged against a
    /// multiple of it (see AvailabilityCalculator.MissingBeatFactor).</summary>
    public int HeartbeatIntervalSeconds { get; set; }

    /// <summary>Chronological, gap-free cover of [From,To]; adjacent equal states are merged.</summary>
    public List<AvailabilityIntervalDto> Intervals { get; set; } = new();

    public double UpMinutes { get; set; }
    public double BusDownMinutes { get; set; }
    public double MonitorDownMinutes { get; set; }

    /// <summary>Minutes not covered by the liveness record — before it began or past its retention.</summary>
    public double UnknownMinutes { get; set; }

    /// <summary>
    /// Intervals where recording provably failed (<see cref="AvailabilityState.BusDown"/> or
    /// <see cref="AvailabilityState.MonitorDown"/>). Excludes <see cref="AvailabilityState.Unknown"/>:
    /// no evidence is not the same as evidence of an outage.
    /// </summary>
    public List<AvailabilityIntervalDto> Outages { get; set; } = new();
}
