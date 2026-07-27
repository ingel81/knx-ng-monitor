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
