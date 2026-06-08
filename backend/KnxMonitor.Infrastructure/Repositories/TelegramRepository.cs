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
            // Keyset on (Timestamp DESC, Id DESC): strictly "older" than the cursor row.
            query = query.Where(t =>
                t.Timestamp < ts || (t.Timestamp == ts && t.Id < id));
        }

        // Fetch one extra row as a sentinel to determine HasMore without a separate count.
        var rows = await query
            .Include(t => t.GroupAddress)
            .OrderByDescending(t => t.Timestamp)
            .ThenByDescending(t => t.Id)
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
        if (filter.Type.HasValue)
        {
            var type = filter.Type.Value;
            query = query.Where(t => t.MessageType == type);
        }

        return query;
    }
}
