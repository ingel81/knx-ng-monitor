using KnxMonitor.Core.Entities;

namespace KnxMonitor.Core.Interfaces;

public interface IMonitorHeartbeatRepository : IRepository<MonitorHeartbeat>
{
    /// <summary>Beats inside [from,to], oldest first.</summary>
    Task<IReadOnlyList<MonitorHeartbeat>> GetRangeAsync(DateTime from, DateTime to, CancellationToken ct = default);

    /// <summary>Last beat at or before <paramref name="instant"/>, or null when none exists.</summary>
    Task<MonitorHeartbeat?> GetLastBeforeAsync(DateTime instant, CancellationToken ct = default);

    /// <summary>
    /// Timestamp of the oldest beat, or null when none exist. Marks where the liveness record
    /// begins: everything before it is unknown, not an outage.
    /// </summary>
    Task<DateTime?> GetEarliestTimestampAsync(CancellationToken ct = default);

    /// <summary>Drops beats older than <paramref name="cutoff"/>. Returns the number removed.</summary>
    Task<int> DeleteOlderThanAsync(DateTime cutoff, CancellationToken ct = default);
}
