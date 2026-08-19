using KnxMonitor.Core.DTOs;
using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Enums;

namespace KnxMonitor.Infrastructure.Services;

/// <summary>
/// Turns raw heartbeats into a gap-free cover of a time range, so a hole in the telegram history
/// can be attributed to a quiet bus, a lost link or a stopped process.
/// </summary>
/// <remarks>
/// Pure and side-effect free (no DB, no clock) so the classification is unit-testable.
/// <para>
/// Anything before the first beat that was ever written is <see cref="AvailabilityState.Unknown"/>,
/// never an outage: after an upgrade the table starts empty, and reporting the whole pre-upgrade
/// history as "the monitor was not running" would be exactly wrong about telegrams that are sitting
/// in the archive. Only a hole <em>between</em> two beats proves the process was gone.
/// </para>
/// <para>
/// Two further conventions worth knowing. A stretch between two beats counts as
/// <see cref="AvailabilityState.Up"/> only when <em>both</em> ends report
/// <see cref="KnxLinkState.Connected"/>: sampling once a minute cannot see a link that dropped and
/// recovered in between, so the calculator errs towards reporting a stretch as incomplete rather
/// than claiming completeness it cannot back up. And a missing beat marks the whole span between
/// its neighbours as <see cref="AvailabilityState.MonitorDown"/>, which overstates the outage by up
/// to one beat interval at each edge — the alternative would be inventing a boundary no data
/// supports.
/// </para>
/// </remarks>
public static class AvailabilityCalculator
{
    /// <summary>
    /// A gap longer than this multiple of the beat interval means "no beat arrived". Three rather
    /// than two because the writer shares the single SQLite writer with the telegram retention
    /// pass: a two-minute write stall under load would otherwise be reported as an outage of a
    /// process that never stopped, which is the one verdict this feature must not get wrong.
    /// </summary>
    private const double MissingBeatFactor = 3.0;

    /// <param name="beats">Beats inside [from,to], ascending by timestamp.</param>
    /// <param name="from">Start of the range (UTC).</param>
    /// <param name="to">End of the range (UTC). Must be greater than <paramref name="from"/>.</param>
    /// <param name="interval">Nominal beat cadence.</param>
    /// <param name="previousBeat">
    /// Last beat at or before <paramref name="from"/>, when known. Lets the leading edge be judged
    /// instead of guessed; without it the first beat's own state is used for the run-up.
    /// </param>
    /// <param name="recordStart">
    /// Timestamp of the oldest beat in the database. Everything before it is reported as
    /// <see cref="AvailabilityState.Unknown"/> instead of an outage. Null means nothing was ever
    /// recorded, which makes the whole range unknown.
    /// </param>
    public static AvailabilityResponse Build(
        IReadOnlyList<MonitorHeartbeat> beats,
        DateTime from,
        DateTime to,
        TimeSpan interval,
        MonitorHeartbeat? previousBeat = null,
        DateTime? recordStart = null)
    {
        var response = new AvailabilityResponse
        {
            From = from,
            To = to,
            HeartbeatIntervalSeconds = (int)interval.TotalSeconds
        };

        if (to <= from)
        {
            return response;
        }

        var maxGap = TimeSpan.FromTicks((long)(interval.Ticks * MissingBeatFactor));
        var ordered = beats.Where(b => b.Timestamp >= from && b.Timestamp <= to)
                           .OrderBy(b => b.Timestamp)
                           .ToList();

        var raw = new List<AvailabilityIntervalDto>();

        // Nothing is claimed about the stretch before the record begins. Clamped to `to` as well,
        // otherwise a window that lies entirely before the record would emit a segment reaching
        // past the queried range and break the gap-free cover of [From,To].
        var knownFrom = recordStart is { } start && start > from ? start : from;
        if (knownFrom > to)
        {
            knownFrom = to;
        }
        if (knownFrom > from)
        {
            raw.Add(Segment(from, knownFrom, AvailabilityState.Unknown, 0));
        }

        if (ordered.Count == 0)
        {
            // No beat inside the window. Only a window that lies inside the recorded period can
            // conclude anything from that — otherwise it is simply not covered.
            if (knownFrom < to)
            {
                raw.Add(Segment(knownFrom, to, recordStart is null
                    ? AvailabilityState.Unknown
                    : AvailabilityState.MonitorDown, 0));
            }
        }
        else
        {
            // Leading edge: [knownFrom .. first beat].
            var first = ordered[0];
            if (first.Timestamp > knownFrom)
            {
                var leadingState = first.Timestamp - knownFrom > maxGap
                    ? AvailabilityState.MonitorDown
                    : Combine(previousBeat?.State ?? first.State, first.State);
                raw.Add(Segment(knownFrom, first.Timestamp, leadingState,
                    leadingState == AvailabilityState.MonitorDown ? 0 : first.TelegramsSinceLast));
            }

            // Between consecutive beats.
            for (var i = 1; i < ordered.Count; i++)
            {
                var prev = ordered[i - 1];
                var current = ordered[i];
                var state = current.Timestamp - prev.Timestamp > maxGap
                    ? AvailabilityState.MonitorDown
                    : Combine(prev.State, current.State);
                raw.Add(Segment(prev.Timestamp, current.Timestamp, state,
                    state == AvailabilityState.MonitorDown ? 0 : current.TelegramsSinceLast));
            }

            // Trailing edge: [last beat .. to]. A tail shorter than the tolerance is simply the
            // gap to the next beat that has not been written yet, not an outage.
            var last = ordered[^1];
            if (to > last.Timestamp)
            {
                var trailingState = to - last.Timestamp > maxGap
                    ? AvailabilityState.MonitorDown
                    : Combine(last.State, last.State);
                raw.Add(Segment(last.Timestamp, to, trailingState, 0));
            }
        }

        response.Intervals = Merge(raw);
        response.UpMinutes = SumMinutes(response.Intervals, AvailabilityState.Up);
        response.BusDownMinutes = SumMinutes(response.Intervals, AvailabilityState.BusDown);
        response.MonitorDownMinutes = SumMinutes(response.Intervals, AvailabilityState.MonitorDown);
        response.UnknownMinutes = SumMinutes(response.Intervals, AvailabilityState.Unknown);
        // Unknown is not an outage: it is the absence of evidence, and listing it as one would
        // turn every fresh install into a week-long incident report.
        response.Outages = response.Intervals
            .Where(i => i.State is AvailabilityState.BusDown or AvailabilityState.MonitorDown)
            .ToList();
        return response;
    }

    private static AvailabilityState Combine(KnxLinkState start, KnxLinkState end) =>
        start == KnxLinkState.Connected && end == KnxLinkState.Connected
            ? AvailabilityState.Up
            : AvailabilityState.BusDown;

    private static AvailabilityIntervalDto Segment(DateTime from, DateTime to, AvailabilityState state, long telegrams) =>
        new()
        {
            From = from,
            To = to,
            State = state,
            Minutes = Math.Round((to - from).TotalMinutes, 1),
            Telegrams = telegrams
        };

    private static List<AvailabilityIntervalDto> Merge(List<AvailabilityIntervalDto> raw)
    {
        var merged = new List<AvailabilityIntervalDto>();
        foreach (var segment in raw)
        {
            if (merged.Count > 0 && merged[^1].State == segment.State)
            {
                var last = merged[^1];
                last.To = segment.To;
                last.Minutes = Math.Round((last.To - last.From).TotalMinutes, 1);
                last.Telegrams += segment.Telegrams;
                continue;
            }
            merged.Add(segment);
        }
        return merged;
    }

    private static double SumMinutes(IEnumerable<AvailabilityIntervalDto> intervals, AvailabilityState state) =>
        Math.Round(intervals.Where(i => i.State == state).Sum(i => (i.To - i.From).TotalMinutes), 1);
}
