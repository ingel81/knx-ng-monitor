using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Interfaces;
using KnxMonitor.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KnxMonitor.Infrastructure.Repositories;

public class MonitorHeartbeatRepository : Repository<MonitorHeartbeat>, IMonitorHeartbeatRepository
{
    public MonitorHeartbeatRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<MonitorHeartbeat>> GetRangeAsync(
        DateTime from, DateTime to, CancellationToken ct = default)
    {
        var rows = await _dbSet
            .Where(h => h.Timestamp >= from && h.Timestamp <= to)
            .OrderBy(h => h.Timestamp)
            .AsNoTracking()
            .ToListAsync(ct);

        foreach (var row in rows)
        {
            TagUtc(row);
        }
        return rows;
    }

    public async Task<MonitorHeartbeat?> GetLastBeforeAsync(DateTime instant, CancellationToken ct = default)
    {
        var row = await _dbSet
            .Where(h => h.Timestamp <= instant)
            .OrderByDescending(h => h.Timestamp)
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);

        return row is null ? null : TagUtc(row);
    }

    /// <summary>
    /// SQLite hands back Kind=Unspecified although the value was written as UTC. Without this the
    /// timestamps serialize without a trailing 'Z' and every client reads them as local time,
    /// shifting every reported outage by the UTC offset.
    /// </summary>
    private static MonitorHeartbeat TagUtc(MonitorHeartbeat beat)
    {
        beat.Timestamp = DateTime.SpecifyKind(beat.Timestamp, DateTimeKind.Utc);
        return beat;
    }

    public async Task<DateTime?> GetEarliestTimestampAsync(CancellationToken ct = default)
    {
        var earliest = await _dbSet.MinAsync(h => (DateTime?)h.Timestamp, ct);
        return earliest is null ? null : DateTime.SpecifyKind(earliest.Value, DateTimeKind.Utc);
    }

    public async Task<int> DeleteOlderThanAsync(DateTime cutoff, CancellationToken ct = default)
    {
        return await _dbSet.Where(h => h.Timestamp < cutoff).ExecuteDeleteAsync(ct);
    }
}
