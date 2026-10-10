using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using KnxMonitor.Core.DTOs;
using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Enums;
using KnxMonitor.Core.Interfaces;
using KnxMonitor.Infrastructure.Data;

namespace KnxMonitor.Infrastructure.Repositories;

public class TelegramRepository : Repository<KnxTelegram>, ITelegramRepository
{
    public TelegramRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<KnxTelegram>> GetByDateRangeAsync(DateTime from, DateTime to)
    {
        return await _dbSet
            .Where(t => t.Timestamp >= from && t.Timestamp <= to)
            .Include(t => t.GroupAddress)
            .OrderByDescending(t => t.Timestamp)
            .ToListAsync();
    }

    public async Task<IEnumerable<KnxTelegram>> GetByGroupAddressAsync(string address)
    {
        return await _dbSet
            .Where(t => t.DestinationAddress == address)
            .Include(t => t.GroupAddress)
            .OrderByDescending(t => t.Timestamp)
            .ToListAsync();
    }

    public async Task<IEnumerable<KnxTelegram>> GetByMessageTypeAsync(MessageType messageType)
    {
        return await _dbSet
            .Where(t => t.MessageType == messageType)
            .Include(t => t.GroupAddress)
            .OrderByDescending(t => t.Timestamp)
            .ToListAsync();
    }

    public async Task<IEnumerable<KnxTelegram>> GetPagedAsync(int page, int pageSize)
    {
        return await _dbSet
            .Include(t => t.GroupAddress)
            .OrderByDescending(t => t.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<int> DeleteAllAsync()
    {
        // Set-based delete: a single SQL statement, no entity tracking / no row load.
        var removed = await _dbSet.ExecuteDeleteAsync();
        // Shrink the WAL after a bulk delete so the file does not stay bloated.
        await CheckpointWalAsync();
        return removed;
    }

    public async Task<int> GetTotalCountAsync()
    {
        return await _dbSet.CountAsync();
    }

    public async Task AddRangeAsync(IEnumerable<KnxTelegram> telegrams)
    {
        await _dbSet.AddRangeAsync(telegrams);
        await _context.SaveChangesAsync();
    }

    // --- Ring-buffer retention ---

    public async Task<int> GetMaxIdAsync()
    {
        return await _dbSet.MaxAsync(t => (int?)t.Id) ?? 0;
    }

    public async Task<int> DeleteOlderThanIdChunkAsync(int thresholdId, int chunkSize)
    {
        // SQLite's DELETE has no LIMIT (unless compiled with SQLITE_ENABLE_UPDATE_DELETE_LIMIT),
        // so bound the work with a subquery over the primary key.
        return await _context.Database.ExecuteSqlRawAsync(
            "DELETE FROM KnxTelegrams WHERE Id IN (SELECT Id FROM KnxTelegrams WHERE Id < {0} ORDER BY Id LIMIT {1})",
            thresholdId, chunkSize);
    }

    public async Task CheckpointWalAsync()
    {
        await _context.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE);");
    }

    // --- History query ---

    public async Task<(IReadOnlyList<KnxTelegram> Items, bool HasMore)> GetPageKeysetAsync(
        TelegramFilter filter, DateTime? cursorTimestamp, int? cursorId, int pageSize)
    {
        var query = BuildFilteredQuery(filter);

        if (cursorTimestamp.HasValue && cursorId.HasValue)
        {
            var ts = cursorTimestamp.Value;
            var id = cursorId.Value;
            // Keyset over (Timestamp, Id): rows strictly "after" the cursor in sort order.
            query = filter.Ascending
                ? query.Where(t => t.Timestamp > ts || (t.Timestamp == ts && t.Id > id))
                : query.Where(t => t.Timestamp < ts || (t.Timestamp == ts && t.Id < id));
        }

        var ordered = filter.Ascending
            ? query.Include(t => t.GroupAddress).OrderBy(t => t.Timestamp).ThenBy(t => t.Id)
            : query.Include(t => t.GroupAddress).OrderByDescending(t => t.Timestamp).ThenByDescending(t => t.Id);

        // Fetch one extra row as a sentinel to determine HasMore without a separate count.
        var rows = await ordered
            .Take(pageSize + 1)
            .ToListAsync();

        var hasMore = rows.Count > pageSize;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        return (rows, hasMore);
    }

    public async Task<int> CountByFilterAsync(TelegramFilter filter)
    {
        return await BuildFilteredQuery(filter).CountAsync();
    }

    public IAsyncEnumerable<KnxTelegram> StreamByFilterAsync(TelegramFilter filter, CancellationToken ct = default)
    {
        return BuildFilteredQuery(filter)
            .Include(t => t.GroupAddress)
            .OrderBy(t => t.Timestamp)
            .ThenBy(t => t.Id)
            .AsNoTracking()
            .AsAsyncEnumerable();
    }

    // --- Charts / statistics ---

    public async Task<(IReadOnlyList<KnxTelegram> Rows, bool Truncated)> GetSeriesRangeAsync(
        IReadOnlyCollection<string> addresses, DateTime from, DateTime to, int maxRows,
        CancellationToken ct = default)
    {
        if (addresses.Count == 0)
        {
            return (Array.Empty<KnxTelegram>(), false);
        }

        var addressList = addresses.ToList();

        // Newest-first with one extra row: the extra tells us the cap was hit without a second
        // COUNT query. Taking the oldest rows instead would silently cut off the recent end of
        // the range, which looks like a complete chart and is the harder error to notice.
        var rows = await _dbSet
            .Where(t => t.Timestamp >= from && t.Timestamp <= to && addressList.Contains(t.DestinationAddress))
            .Include(t => t.GroupAddress)
            .OrderByDescending(t => t.Timestamp)
            .ThenByDescending(t => t.Id)
            .Take(maxRows + 1)
            .AsNoTracking()
            .ToListAsync(ct);

        var truncated = rows.Count > maxRows;
        if (truncated)
        {
            rows.RemoveRange(maxRows, rows.Count - maxRows);
        }

        // Callers expect chronological order.
        rows.Reverse();
        return (rows, truncated);
    }

    /// <summary>A value older than this is not carried into a chart range.</summary>
    private const int CarryMaxAgeDays = 30;

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<KnxTelegram>>> GetLatestBeforeAsync(
        IReadOnlyCollection<string> addresses, DateTime before, int perAddress,
        CancellationToken ct = default)
    {
        var result = new Dictionary<string, IReadOnlyList<KnxTelegram>>(StringComparer.Ordinal);

        // One small query per address (at most 8) instead of one grouped query: SQLite walks the
        // (Timestamp, DestinationAddress) index backwards from `before` and checks the address on
        // the index entry itself, so only the matching rows are ever read from the table.
        // Bounded look-back: for an address that never sent, an open-ended search walked the whole
        // index backwards (tens of ms per address on a desktop, far more on a Pi).
        var since = before.AddDays(-CarryMaxAgeDays);
        foreach (var address in addresses)
        {
            var rows = await _dbSet
                .Where(t => t.DestinationAddress == address && t.Timestamp < before && t.Timestamp >= since)
                .OrderByDescending(t => t.Timestamp)
                .ThenByDescending(t => t.Id)
                .Take(perAddress)
                // The name of a series that has only this carried-in point comes from here.
                .Include(t => t.GroupAddress)
                .AsNoTracking()
                .ToListAsync(ct);

            if (rows.Count > 0)
            {
                result[address] = rows;
            }
        }
        return result;
    }

    public async Task<(DateTime Oldest, DateTime Newest)?> GetTimestampBoundsAsync(CancellationToken ct = default)
    {
        // Both ends come straight off the Timestamp index.
        var oldest = await _dbSet.OrderBy(t => t.Timestamp).Select(t => (DateTime?)t.Timestamp).FirstOrDefaultAsync(ct);
        if (oldest is null)
        {
            return null;
        }
        var newest = await _dbSet.OrderByDescending(t => t.Timestamp).Select(t => t.Timestamp).FirstAsync(ct);
        return (oldest.Value, newest);
    }

    public async Task<int> CountInRangeAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        return await _dbSet
            .Where(t => t.Timestamp >= from && t.Timestamp <= to)
            .CountAsync(ct);
    }

    public IAsyncEnumerable<DateTime> StreamTimestampsInRangeAsync(
        DateTime from, DateTime to, CancellationToken ct = default)
    {
        return _dbSet
            .AsNoTracking()
            .Where(t => t.Timestamp >= from && t.Timestamp <= to)
            .OrderBy(t => t.Timestamp)
            .Select(t => t.Timestamp)
            .AsAsyncEnumerable();
    }

    // --- SQL aggregation for the statistics view (#39) ---
    //
    // The counts come from index range counts, not from computing a bucket per row: SQLite has to
    // evaluate any per-row expression in its VM, and turning the TEXT timestamp into a number
    // (strftime + substr) cost more than streaming the rows to .NET did. Instead the bucket edges
    // are worked out in C# with the exact formula of the former .NET loop and each bucket becomes
    // one "Timestamp >= lo AND Timestamp < hi" count over the (Timestamp, DestinationAddress)
    // index. All of them run in a single statement via json_each.
    //
    // EF stores DateTime as TEXT "yyyy-MM-dd HH:mm:ss.FFFFFFF" (trailing zeros trimmed), and on
    // that format text order is time order. That is what the existing range filters rely on, and
    // the edges below are formatted the same way.

    private const string StoredTimestampFormat = "yyyy-MM-dd HH:mm:ss.FFFFFFF";

    private sealed class RangeCountRow
    {
        public long Idx { get; set; }
        public long Cnt { get; set; }
    }

    /// <summary>
    /// Counts telegrams in each half-open tick range [lo, hi), all in one statement. The
    /// correlated subquery is a plain range count, so each range costs one index seek plus a walk
    /// over its own entries.
    /// </summary>
    private async Task<long[]> CountInRangesAsync(
        IReadOnlyList<(long Lo, long Hi)> ranges, CancellationToken ct)
    {
        var counts = new long[ranges.Count];
        if (ranges.Count == 0) return counts;

        var json = new System.Text.StringBuilder("[");
        for (var i = 0; i < ranges.Count; i++)
        {
            if (i > 0) json.Append(',');
            json.Append("[\"")
                .Append(new DateTime(ranges[i].Lo).ToString(StoredTimestampFormat, System.Globalization.CultureInfo.InvariantCulture))
                .Append("\",\"")
                .Append(new DateTime(ranges[i].Hi).ToString(StoredTimestampFormat, System.Globalization.CultureInfo.InvariantCulture))
                .Append("\"]");
        }
        json.Append(']');

        var rows = await _context.Database
            .SqlQueryRaw<RangeCountRow>(
                "SELECT CAST(r.key AS INTEGER) AS Idx,"
                + " (SELECT COUNT(*) FROM KnxTelegrams t"
                + "  WHERE t.Timestamp >= json_extract(r.value, '$[0]')"
                + "    AND t.Timestamp < json_extract(r.value, '$[1]')) AS Cnt"
                + " FROM json_each(@ranges) r",
                new SqliteParameter("@ranges", json.ToString()))
            .ToListAsync(ct);

        foreach (var row in rows)
        {
            counts[row.Idx] = row.Cnt;
        }
        return counts;
    }

    public async Task<long[]> CountPerBucketAsync(
        DateTime from, DateTime to, int bucketCount, double bucketMs, CancellationToken ct = default)
    {
        // The former loop: idx = (int)((ts - from).TotalMilliseconds / bucketMs), clamped to
        // [0, bucketCount - 1]. idx only grows with ts, so bucket i starts at the first tick whose
        // idx reaches i. Found from the estimate by stepping single ticks with the very same
        // double arithmetic, which keeps every telegram in the bucket the loop would have chosen.
        int IndexOf(long ticks) => (int)(TimeSpan.FromTicks(ticks - from.Ticks).TotalMilliseconds / bucketMs);

        // [from, to] inclusive == [from, to + 1 tick); capped so to = DateTime.MaxValue cannot overflow.
        var end = Math.Min(to.Ticks + 1, DateTime.MaxValue.Ticks);
        var edges = new long[bucketCount + 1];
        edges[0] = from.Ticks;
        edges[bucketCount] = end;
        for (var i = 1; i < bucketCount; i++)
        {
            var t = from.Ticks + (long)Math.Ceiling(i * bucketMs * TimeSpan.TicksPerMillisecond);
            while (t > from.Ticks && IndexOf(t - 1) >= i) t--;
            while (t < end && IndexOf(t) < i) t++;
            edges[i] = Math.Clamp(t, edges[i - 1], end);
        }

        var ranges = new (long, long)[bucketCount];
        for (var i = 0; i < bucketCount; i++) ranges[i] = (edges[i], edges[i + 1]);

        return await CountInRangesAsync(ranges, ct);
    }

    public async Task<long[][]> CountPerWeekdayHourAsync(
        DateTime from, DateTime to, int tzOffsetMinutes, CancellationToken ct = default)
    {
        var grid = new long[7][];
        for (var d = 0; d < 7; d++) grid[d] = new long[24];

        // One range per local clock hour the range touches. A telegram's local time is its UTC
        // time minus the offset (the former loop did ts.AddMinutes(-tz)), so local hour slot s
        // spans UTC ticks [s * hour + shift, (s + 1) * hour + shift).
        var shift = tzOffsetMinutes * TimeSpan.TicksPerMinute;
        var end = to.Ticks + 1;
        var firstSlot = (from.Ticks - shift) / TimeSpan.TicksPerHour;
        var lastSlot = (to.Ticks - shift) / TimeSpan.TicksPerHour;

        var slots = new List<long>();
        var ranges = new List<(long, long)>();
        for (var s = firstSlot; s <= lastSlot; s++)
        {
            var lo = Math.Max(from.Ticks, s * TimeSpan.TicksPerHour + shift);
            var hi = Math.Min(end, (s + 1) * TimeSpan.TicksPerHour + shift);
            if (hi <= lo) continue;
            slots.Add(s);
            ranges.Add((lo, hi));
        }

        var counts = await CountInRangesAsync(ranges, ct);
        for (var i = 0; i < slots.Count; i++)
        {
            var local = new DateTime(slots[i] * TimeSpan.TicksPerHour);
            grid[(int)local.DayOfWeek][local.Hour] += counts[i];
        }

        return grid;
    }

    public async Task<IReadOnlyList<AddressCount>> GetTopDestinationsAsync(
        DateTime from, DateTime to, int limit, CancellationToken ct = default)
    {
        return await _dbSet
            .AsNoTracking()
            .Where(t => t.Timestamp >= from && t.Timestamp <= to)
            .GroupBy(t => t.DestinationAddress)
            .Select(g => new AddressCount { Address = g.Key, Count = g.LongCount() })
            .OrderByDescending(a => a.Count)
            .ThenBy(a => a.Address)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AddressCount>> GetTopSourcesAsync(
        DateTime from, DateTime to, int limit, CancellationToken ct = default)
    {
        return await _dbSet
            .AsNoTracking()
            .Where(t => t.Timestamp >= from && t.Timestamp <= to)
            .GroupBy(t => t.SourceAddress)
            .Select(g => new AddressCount { Address = g.Key, Count = g.LongCount() })
            .OrderByDescending(a => a.Count)
            .ThenBy(a => a.Address)
            .Take(limit)
            .ToListAsync(ct);
    }

    private IQueryable<KnxTelegram> BuildFilteredQuery(TelegramFilter filter)
    {
        var query = _dbSet.AsQueryable();

        if (filter.From.HasValue)
        {
            var from = filter.From.Value;
            query = query.Where(t => t.Timestamp >= from);
        }
        if (filter.To.HasValue)
        {
            var to = filter.To.Value;
            query = query.Where(t => t.Timestamp <= to);
        }
        if (!string.IsNullOrWhiteSpace(filter.Address))
        {
            query = query.Where(t => t.DestinationAddress == filter.Address);
        }
        if (!string.IsNullOrWhiteSpace(filter.Source))
        {
            query = query.Where(t => t.SourceAddress == filter.Source);
        }
        if (filter.Types is { Count: > 0 })
        {
            var types = filter.Types;
            query = query.Where(t => types.Contains(t.MessageType));
        }
        else if (filter.Type.HasValue)
        {
            var type = filter.Type.Value;
            query = query.Where(t => t.MessageType == type);
        }
        if (!string.IsNullOrWhiteSpace(filter.Q))
        {
            // Free-text OR across text fields (incl. GA name/DPT via navigation).
            var like = $"%{filter.Q.Trim()}%";
            query = query.Where(t =>
                EF.Functions.Like(t.SourceAddress, like) ||
                EF.Functions.Like(t.DestinationAddress, like) ||
                (t.ValueDecoded != null && EF.Functions.Like(t.ValueDecoded, like)) ||
                (t.GroupAddress != null && EF.Functions.Like(t.GroupAddress.Name, like)) ||
                (t.GroupAddress != null && t.GroupAddress.DatapointType != null &&
                    EF.Functions.Like(t.GroupAddress.DatapointType, like)));
        }

        return query;
    }
}
