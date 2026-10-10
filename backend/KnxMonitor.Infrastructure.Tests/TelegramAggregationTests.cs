using FluentAssertions;
using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Enums;
using KnxMonitor.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace KnxMonitor.Infrastructure.Tests;

/// <summary>
/// #39 moved the statistics histogram and the heatmap from "stream every timestamp and count in
/// .NET" to GROUP BY in SQLite. The numbers must not move by a single telegram, so every test here
/// runs the former .NET counting (kept verbatim below) over the same database and compares.
/// </summary>
public class TelegramAggregationTests
{
    private static readonly DateTime Start = new(2026, 3, 28, 21, 13, 7, 123, DateTimeKind.Utc);

    private static KnxTelegram Tg(DateTime ts) => new()
    {
        Timestamp = ts,
        DestinationAddress = "1/1/1",
        SourceAddress = "1.1.1",
        MessageType = MessageType.Write,
        Value = new byte[] { 0x01 }
    };

    /// <summary>
    /// Timestamps that hit the awkward cases: whole seconds (stored without a fraction), every
    /// fraction length from 1 to 7 digits, the last tick before a full hour and before midnight,
    /// exact range edges and random instants across three days.
    /// </summary>
    private static List<DateTime> BuildTimestamps(DateTime from, DateTime to, int buckets)
    {
        var list = new List<DateTime> { from, to, from.AddTicks(-1), to.AddTicks(1) };
        var rnd = new Random(39);
        var spanTicks = (to - from).Ticks;

        for (var i = 0; i < 3000; i++)
        {
            list.Add(from.AddTicks((long)(rnd.NextDouble() * spanTicks)));
        }

        // Fraction lengths 0..7 digits after EF's trailing-zero trim.
        foreach (var extra in new long[] { 0, 1_000_000, 1_200_000, 1_230_000, 1_234_000, 1_234_500, 1_234_560, 1_234_567 })
        {
            list.Add(new DateTime(2026, 3, 29, 10, 0, 0, DateTimeKind.Utc).AddTicks(extra));
        }

        // Hour and day edges in UTC and in the shifted zones the heatmap tests use.
        var day = new DateTime(2026, 3, 29, 0, 0, 0, DateTimeKind.Utc);
        for (var h = -6; h <= 30; h++)
        {
            var edge = day.AddHours(h);
            list.Add(edge);
            list.Add(edge.AddTicks(-1));
            list.Add(edge.AddMinutes(30));
            list.Add(edge.AddMinutes(30).AddTicks(-1));
        }

        // Bucket edges: exactly on, one tick before, one tick after.
        var bucketTicks = (double)spanTicks / buckets;
        for (var b = 0; b <= buckets; b++)
        {
            var edge = from.AddTicks((long)(bucketTicks * b));
            list.Add(edge);
            list.Add(edge.AddTicks(-1));
            list.Add(edge.AddTicks(1));
        }

        return list;
    }

    private static async Task<long[]> LegacyBucketsAsync(
        ITelegramRepository repo, DateTime rangeFrom, DateTime rangeTo, int bucketCount, double bucketMs)
    {
        var counts = new long[bucketCount];
        await foreach (var ts in repo.StreamTimestampsInRangeAsync(rangeFrom, rangeTo))
        {
            var offset = (ts - rangeFrom).TotalMilliseconds;
            var idx = (int)(offset / bucketMs);
            if (idx < 0) idx = 0;
            if (idx >= bucketCount) idx = bucketCount - 1;
            counts[idx]++;
        }
        return counts;
    }

    private static async Task<long[][]> LegacyHeatmapAsync(
        ITelegramRepository repo, DateTime rangeFrom, DateTime rangeTo, int tz)
    {
        var grid = new long[7][];
        for (var d = 0; d < 7; d++) grid[d] = new long[24];
        await foreach (var ts in repo.StreamTimestampsInRangeAsync(rangeFrom, rangeTo))
        {
            var local = DateTime.SpecifyKind(ts, DateTimeKind.Utc).AddMinutes(-tz);
            grid[(int)local.DayOfWeek][local.Hour]++;
        }
        return grid;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(60)]
    [InlineData(500)]
    public async Task Buckets_match_the_former_dotnet_counting(int bucketCount)
    {
        using var db = new SqliteTestDb();
        using var scope = db.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITelegramRepository>();

        // Deliberately odd range so bucketMs is fractional.
        var from = Start;
        var to = Start.AddDays(3).AddMilliseconds(777).AddTicks(4321);
        await repo.AddRangeAsync(BuildTimestamps(from, to, bucketCount).Select(Tg));

        var totalMs = (to - from).TotalMilliseconds;
        var bucketMs = totalMs / bucketCount;

        var expected = await LegacyBucketsAsync(repo, from, to, bucketCount, bucketMs);
        var actual = await repo.CountPerBucketAsync(from, to, bucketCount, bucketMs);

        actual.Should().Equal(expected);
        actual.Sum().Should().Be(await repo.CountInRangeAsync(from, to));
    }

    [Fact]
    public async Task Buckets_of_an_empty_range_are_all_zero()
    {
        using var db = new SqliteTestDb();
        using var scope = db.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITelegramRepository>();

        var counts = await repo.CountPerBucketAsync(Start, Start.AddHours(1), 60, 60_000);

        counts.Should().HaveCount(60).And.OnlyContain(c => c == 0);
    }

    [Fact]
    public async Task Top_lists_rank_by_count_break_ties_by_address_and_respect_range_and_limit()
    {
        using var db = new SqliteTestDb();
        using var scope = db.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITelegramRepository>();

        var from = Start;
        var to = Start.AddHours(1);
        var rows = new List<KnxTelegram>();
        void Add(string dst, string src, int n, DateTime? at = null)
        {
            for (var i = 0; i < n; i++)
            {
                var t = Tg(at ?? from.AddSeconds(i + 1));
                t.DestinationAddress = dst;
                t.SourceAddress = src;
                rows.Add(t);
            }
        }

        Add("1/1/3", "1.1.9", 5);
        Add("1/1/1", "1.1.2", 3);
        Add("1/1/2", "1.1.2", 3);     // tie with 1/1/1 on count
        Add("9/9/9", "9.9.9", 50, to.AddSeconds(1)); // outside the range
        Add("1/1/4", "1.1.1", 1, to); // exactly at the inclusive end
        await repo.AddRangeAsync(rows);

        var top = await repo.GetTopDestinationsAsync(from, to, 3);
        top.Select(a => (a.Address, a.Count)).Should().Equal(("1/1/3", 5L), ("1/1/1", 3L), ("1/1/2", 3L));

        var senders = await repo.GetTopSourcesAsync(from, to, 10);
        senders.Select(a => (a.Address, a.Count)).Should().Equal(("1.1.2", 6L), ("1.1.9", 5L), ("1.1.1", 1L));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-120)]   // CEST
    [InlineData(-60)]    // CET
    [InlineData(300)]    // US Eastern
    [InlineData(-330)]   // India, half-hour offset
    [InlineData(-345)]   // Nepal, quarter-hour offset
    public async Task Heatmap_matches_the_former_dotnet_counting(int tz)
    {
        using var db = new SqliteTestDb();
        using var scope = db.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITelegramRepository>();

        var from = Start;
        var to = Start.AddDays(3);
        await repo.AddRangeAsync(BuildTimestamps(from, to, 60).Select(Tg));

        var expected = await LegacyHeatmapAsync(repo, from, to, tz);
        var actual = await repo.CountPerWeekdayHourAsync(from, to, tz);

        actual.Should().HaveCount(7);
        for (var d = 0; d < 7; d++)
        {
            actual[d].Should().Equal(expected[d], $"weekday {d} with tz {tz}");
        }
    }
}
