using FluentAssertions;
using KnxMonitor.Core.DTOs;
using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Enums;
using KnxMonitor.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace KnxMonitor.Infrastructure.Tests;

public class TelegramRepositoryTests
{
    private static KnxTelegram Tg(DateTime ts, string dst = "1/1/1", string src = "1.1.1",
        MessageType mt = MessageType.Write)
        => new()
        {
            Timestamp = ts,
            DestinationAddress = dst,
            SourceAddress = src,
            MessageType = mt,
            Value = new byte[] { 0x01 }
        };

    [Fact]
    public async Task Retention_keeps_last_N_by_id()
    {
        using var db = new SqliteTestDb();
        using var scope = db.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITelegramRepository>();

        var baseTs = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await repo.AddRangeAsync(Enumerable.Range(0, 100).Select(i => Tg(baseTs.AddSeconds(i))));

        (await repo.GetMaxIdAsync()).Should().Be(100);

        // Keep the newest 10 → delete everything with Id < (100 - 10) = 90.
        var threshold = 90;
        int deleted;
        do { deleted = await repo.DeleteOlderThanIdChunkAsync(threshold, 5); }
        while (deleted == 5);

        (await repo.GetTotalCountAsync()).Should().Be(11); // Ids 90..100
    }

    [Fact]
    public async Task Keyset_pagination_has_no_gaps_or_dupes_with_equal_timestamps()
    {
        using var db = new SqliteTestDb();
        using var scope = db.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITelegramRepository>();

        // All 25 share the same timestamp → forces the Id tiebreaker.
        var ts = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        await repo.AddRangeAsync(Enumerable.Range(0, 25).Select(_ => Tg(ts)));

        var ids = new List<int>();
        DateTime? cursorTs = null;
        int? cursorId = null;
        while (true)
        {
            var (items, hasMore) = await repo.GetPageKeysetAsync(new TelegramFilter(), cursorTs, cursorId, 10);
            ids.AddRange(items.Select(i => i.Id));
            if (items.Count > 0)
            {
                cursorTs = items[^1].Timestamp;
                cursorId = items[^1].Id;
            }
            if (!hasMore) break;
        }

        ids.Should().HaveCount(25);
        ids.Should().OnlyHaveUniqueItems();
        ids.Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task Filter_composition_matches_expected_rows()
    {
        using var db = new SqliteTestDb();
        using var scope = db.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITelegramRepository>();

        var ts = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await repo.AddRangeAsync(new[]
        {
            Tg(ts, dst: "1/1/1", src: "1.1.1", mt: MessageType.Write),
            Tg(ts.AddMinutes(1), dst: "1/1/2", src: "1.1.2", mt: MessageType.Read),
            Tg(ts.AddMinutes(2), dst: "1/1/1", src: "1.1.3", mt: MessageType.Read)
        });

        (await repo.CountByFilterAsync(new TelegramFilter { Address = "1/1/1" })).Should().Be(2);
        (await repo.CountByFilterAsync(new TelegramFilter { Type = MessageType.Read })).Should().Be(2);
        (await repo.CountByFilterAsync(new TelegramFilter { Address = "1/1/1", Type = MessageType.Read }))
            .Should().Be(1);
        (await repo.CountByFilterAsync(new TelegramFilter { Source = "1.1.2" })).Should().Be(1);
        (await repo.CountByFilterAsync(new TelegramFilter { From = ts.AddMinutes(1) })).Should().Be(2);
    }

    [Fact]
    public async Task StreamByFilterAsync_returns_chronological_order()
    {
        using var db = new SqliteTestDb();
        using var scope = db.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITelegramRepository>();

        var ts = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await repo.AddRangeAsync(new[]
        {
            Tg(ts.AddMinutes(2)),
            Tg(ts),
            Tg(ts.AddMinutes(1))
        });

        var streamed = new List<DateTime>();
        await foreach (var t in repo.StreamByFilterAsync(new TelegramFilter()))
        {
            streamed.Add(t.Timestamp);
        }

        streamed.Should().BeInAscendingOrder();
        streamed.Should().HaveCount(3);
    }

    [Fact]
    public async Task Series_range_returns_everything_in_order_when_under_the_cap()
    {
        using var db = new SqliteTestDb();
        using var scope = db.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITelegramRepository>();

        var baseTs = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await repo.AddRangeAsync(Enumerable.Range(0, 10).Select(i => Tg(baseTs.AddMinutes(i))));

        var (rows, truncated) = await repo.GetSeriesRangeAsync(
            new[] { "1/1/1" }, baseTs.AddMinutes(-1), baseTs.AddHours(1), maxRows: 100);

        truncated.Should().BeFalse();
        rows.Should().HaveCount(10);
        rows.Select(r => r.Timestamp).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Series_range_keeps_the_newest_rows_when_capped_and_says_so()
    {
        using var db = new SqliteTestDb();
        using var scope = db.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITelegramRepository>();

        var baseTs = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await repo.AddRangeAsync(Enumerable.Range(0, 20).Select(i => Tg(baseTs.AddMinutes(i))));

        var (rows, truncated) = await repo.GetSeriesRangeAsync(
            new[] { "1/1/1" }, baseTs.AddMinutes(-1), baseTs.AddHours(1), maxRows: 5);

        truncated.Should().BeTrue("the caller has to be able to tell the user the range is incomplete");
        rows.Should().HaveCount(5);
        // Newest five (minutes 15..19), still chronological. Dropping the recent end instead
        // would produce a chart that looks complete but silently stops short of now.
        rows.First().Timestamp.Should().Be(baseTs.AddMinutes(15));
        rows.Last().Timestamp.Should().Be(baseTs.AddMinutes(19));
        rows.Select(r => r.Timestamp).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Series_range_filters_by_address_and_window()
    {
        using var db = new SqliteTestDb();
        using var scope = db.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITelegramRepository>();

        var baseTs = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await repo.AddRangeAsync(new[]
        {
            Tg(baseTs, "1/1/1"),
            Tg(baseTs.AddMinutes(5), "1/1/2"),
            Tg(baseTs.AddMinutes(10), "1/1/1"),
            Tg(baseTs.AddHours(5), "1/1/1")
        });

        var (rows, _) = await repo.GetSeriesRangeAsync(
            new[] { "1/1/1" }, baseTs, baseTs.AddHours(1), maxRows: 100);

        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(r => r.DestinationAddress == "1/1/1");
    }
}
