using FluentAssertions;
using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Enums;
using KnxMonitor.Infrastructure.Services;

namespace KnxMonitor.Infrastructure.Tests;

/// <summary>
/// The calculator is what turns "there is a hole in my history" into an answer, so the four
/// verdicts it can reach are pinned down here: quiet bus, lost link, process not running, and
/// nothing recorded at all — the last one must never be dressed up as an outage.
/// </summary>
public class AvailabilityCalculatorTests
{
    private static readonly DateTime Origin = new(2026, 8, 18, 20, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private static MonitorHeartbeat Beat(int minute, KnxLinkState state = KnxLinkState.Connected, int telegrams = 0) =>
        new()
        {
            Timestamp = Origin.AddMinutes(minute),
            State = state,
            TelegramsSinceLast = telegrams
        };

    [Fact]
    public void Continuous_connected_beats_report_one_up_interval()
    {
        var beats = Enumerable.Range(0, 11).Select(m => Beat(m, telegrams: 3)).ToList();

        var result = AvailabilityCalculator.Build(beats, Origin, Origin.AddMinutes(10), Interval);

        result.Intervals.Should().ContainSingle();
        result.Intervals[0].State.Should().Be(AvailabilityState.Up);
        result.UpMinutes.Should().Be(10);
        result.MonitorDownMinutes.Should().Be(0);
        result.Outages.Should().BeEmpty();
        // Ten stretches between eleven beats, three telegrams each.
        result.Intervals[0].Telegrams.Should().Be(30);
    }

    [Fact]
    public void Missing_beats_report_the_gap_as_monitor_down()
    {
        // Alive for two minutes, gone for ten, back for two.
        var beats = new List<MonitorHeartbeat> { Beat(0), Beat(1), Beat(2), Beat(12), Beat(13) };

        var result = AvailabilityCalculator.Build(beats, Origin, Origin.AddMinutes(13), Interval);

        result.Outages.Should().ContainSingle();
        var outage = result.Outages[0];
        outage.State.Should().Be(AvailabilityState.MonitorDown);
        outage.From.Should().Be(Origin.AddMinutes(2));
        outage.To.Should().Be(Origin.AddMinutes(12));
        outage.Minutes.Should().Be(10);
        result.MonitorDownMinutes.Should().Be(10);
        result.UpMinutes.Should().Be(3);
    }

    [Fact]
    public void Disconnected_beats_report_bus_down_not_monitor_down()
    {
        var beats = new List<MonitorHeartbeat>
        {
            Beat(0),
            Beat(1),
            Beat(2, KnxLinkState.Disconnected),
            Beat(3, KnxLinkState.Disconnected),
            Beat(4)
        };

        var result = AvailabilityCalculator.Build(beats, Origin, Origin.AddMinutes(4), Interval);

        result.Outages.Should().ContainSingle();
        result.Outages[0].State.Should().Be(AvailabilityState.BusDown);
        result.MonitorDownMinutes.Should().Be(0);
        // Conservative by design: a stretch counts as up only when both ends report Connected,
        // so the minute leading into the drop and the one recovering from it are both flagged.
        result.BusDownMinutes.Should().Be(3);
        result.UpMinutes.Should().Be(1);
    }

    [Fact]
    public void Range_without_any_recording_is_unknown_not_an_outage()
    {
        // The state right after an upgrade: the table exists but nothing was ever written. Calling
        // this an outage would claim the monitor was down for a week it demonstrably recorded.
        var result = AvailabilityCalculator.Build(
            Array.Empty<MonitorHeartbeat>(), Origin, Origin.AddHours(12), Interval);

        result.Intervals.Should().ContainSingle();
        result.Intervals[0].State.Should().Be(AvailabilityState.Unknown);
        result.UnknownMinutes.Should().Be(720);
        result.MonitorDownMinutes.Should().Be(0);
        result.Outages.Should().BeEmpty();
    }

    [Fact]
    public void Time_before_the_record_starts_is_unknown_the_rest_is_judged()
    {
        // Recording began at minute 10; the window starts an hour earlier.
        var recordStart = Origin.AddMinutes(10);
        var beats = new List<MonitorHeartbeat> { Beat(10), Beat(11), Beat(12) };

        var result = AvailabilityCalculator.Build(
            beats, Origin.AddMinutes(-60), Origin.AddMinutes(12), Interval, null, recordStart);

        result.Intervals[0].State.Should().Be(AvailabilityState.Unknown);
        result.Intervals[0].To.Should().Be(recordStart);
        result.UnknownMinutes.Should().Be(70);
        result.Intervals[^1].State.Should().Be(AvailabilityState.Up);
        result.Outages.Should().BeEmpty();
    }

    [Fact]
    public void Empty_window_inside_the_recorded_period_still_counts_as_monitor_down()
    {
        // Recording started long ago, but no beat landed in this window: the process was gone.
        var result = AvailabilityCalculator.Build(
            Array.Empty<MonitorHeartbeat>(), Origin, Origin.AddHours(3), Interval,
            null, Origin.AddDays(-2));

        result.Intervals.Should().ContainSingle();
        result.Intervals[0].State.Should().Be(AvailabilityState.MonitorDown);
        result.MonitorDownMinutes.Should().Be(180);
        result.Outages.Should().ContainSingle();
    }

    [Fact]
    public void Window_entirely_before_the_record_stays_inside_the_queried_range()
    {
        // Heartbeats only exist since today; the archive filter asks about last month. The answer
        // must be "unknown for exactly that window", not an interval running up to the record start.
        var recordStart = Origin.AddDays(30);

        var result = AvailabilityCalculator.Build(
            Array.Empty<MonitorHeartbeat>(), Origin, Origin.AddHours(24), Interval, null, recordStart);

        result.Intervals.Should().ContainSingle();
        result.Intervals[0].State.Should().Be(AvailabilityState.Unknown);
        result.Intervals[0].To.Should().Be(Origin.AddHours(24));
        result.UnknownMinutes.Should().Be(1440);
        result.Outages.Should().BeEmpty();
    }

    [Fact]
    public void Leading_edge_uses_the_beat_before_the_window_when_available()
    {
        var previous = Beat(-1, KnxLinkState.Disconnected);
        var beats = new List<MonitorHeartbeat> { Beat(1), Beat(2) };

        var result = AvailabilityCalculator.Build(beats, Origin, Origin.AddMinutes(2), Interval, previous);

        // The link was down going into the window, so the run-up cannot be claimed as complete.
        result.Intervals[0].State.Should().Be(AvailabilityState.BusDown);
        result.Intervals[0].From.Should().Be(Origin);
        result.Intervals[0].To.Should().Be(Origin.AddMinutes(1));
    }

    [Fact]
    public void Trailing_gap_within_tolerance_is_not_an_outage()
    {
        var beats = new List<MonitorHeartbeat> { Beat(0), Beat(1) };

        // 90s after the last beat: the next beat is merely not due yet.
        var result = AvailabilityCalculator.Build(beats, Origin, Origin.AddSeconds(150), Interval);

        result.Outages.Should().BeEmpty();
        result.UpMinutes.Should().Be(2.5);
    }

    [Fact]
    public void Adjacent_equal_states_are_merged_into_one_interval()
    {
        var beats = Enumerable.Range(0, 5).Select(m => Beat(m)).ToList();
        beats.Add(Beat(20));
        beats.Add(Beat(21));

        var result = AvailabilityCalculator.Build(beats, Origin, Origin.AddMinutes(21), Interval);

        result.Intervals.Select(i => i.State).Should().Equal(
            AvailabilityState.Up, AvailabilityState.MonitorDown, AvailabilityState.Up);
    }

    [Fact]
    public void Reversed_range_yields_nothing_instead_of_negative_minutes()
    {
        var result = AvailabilityCalculator.Build(
            new List<MonitorHeartbeat> { Beat(0) }, Origin.AddMinutes(10), Origin, Interval);

        result.Intervals.Should().BeEmpty();
        result.MonitorDownMinutes.Should().Be(0);
    }
}
