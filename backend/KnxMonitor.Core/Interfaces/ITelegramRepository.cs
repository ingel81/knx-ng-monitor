using KnxMonitor.Core.DTOs;
using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Enums;

namespace KnxMonitor.Core.Interfaces;

public interface ITelegramRepository : IRepository<KnxTelegram>
{
    Task<IEnumerable<KnxTelegram>> GetByDateRangeAsync(DateTime from, DateTime to);
    Task<IEnumerable<KnxTelegram>> GetByGroupAddressAsync(string address);
    Task<IEnumerable<KnxTelegram>> GetByMessageTypeAsync(MessageType messageType);
    Task<IEnumerable<KnxTelegram>> GetPagedAsync(int page, int pageSize);
    Task<int> GetTotalCountAsync();
    Task AddRangeAsync(IEnumerable<KnxTelegram> telegrams);

    // --- Ring-buffer retention (called by the persistence worker, single writer) ---

    /// <summary>Largest telegram Id currently stored, or 0 if the table is empty.</summary>
    Task<int> GetMaxIdAsync();

    /// <summary>Deletes up to <paramref name="chunkSize"/> rows with Id below the threshold. Returns rows removed.</summary>
    Task<int> DeleteOlderThanIdChunkAsync(int thresholdId, int chunkSize);

    /// <summary>Runs PRAGMA wal_checkpoint(TRUNCATE) to cap the WAL file size.</summary>
    Task CheckpointWalAsync();

    // --- History query (keyset pagination, count, streaming export) ---

    /// <summary>
    /// Returns one keyset page ordered by (Timestamp DESC, Id DESC). The cursor is the
    /// (Timestamp, Id) of the last row of the previous page; null for the first page.
    /// </summary>
    Task<(IReadOnlyList<KnxTelegram> Items, bool HasMore)> GetPageKeysetAsync(
        TelegramFilter filter, DateTime? cursorTimestamp, int? cursorId, int pageSize);

    Task<int> CountByFilterAsync(TelegramFilter filter);

    /// <summary>Streams the filtered range chronologically (Timestamp ASC) without buffering all rows.</summary>
    IAsyncEnumerable<KnxTelegram> StreamByFilterAsync(TelegramFilter filter, CancellationToken ct = default);
}
