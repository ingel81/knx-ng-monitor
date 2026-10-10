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

    /// <summary>Deletes the entire telegram history (set-based) and checkpoints the WAL. Returns rows removed.</summary>
    Task<int> DeleteAllAsync();

    /// <summary>Streams the filtered range chronologically (Timestamp ASC) without buffering all rows.</summary>
    IAsyncEnumerable<KnxTelegram> StreamByFilterAsync(TelegramFilter filter, CancellationToken ct = default);

    // --- Charts / statistics ---

    /// <summary>
    /// Returns telegrams for the given destination addresses within [from,to], ordered by
    /// Timestamp ASC. Uses the (Timestamp, DestinationAddress) index. Includes the GroupAddress
    /// navigation for name/DPT lookup.
    /// <para>
    /// Bounded by <paramref name="maxRows"/>, keeping the <b>newest</b> rows: a chart that stops
    /// short of "now" is obviously incomplete, whereas one missing its recent end looks correct
    /// and is not. <c>Truncated</c> reports whether the cap was hit so callers can say so.
    /// </para>
    /// </summary>
    Task<(IReadOnlyList<KnxTelegram> Rows, bool Truncated)> GetSeriesRangeAsync(
        IReadOnlyCollection<string> addresses, DateTime from, DateTime to, int maxRows,
        CancellationToken ct = default);

    /// <summary>
    /// For each address, up to <paramref name="perAddress"/> telegrams strictly before
    /// <paramref name="before"/>, newest first. Lets the chart carry the last known value into the
    /// start of a range; several rows because the newest one may carry no value (a read request).
    /// Addresses without any earlier telegram are absent from the result.
    /// </summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<KnxTelegram>>> GetLatestBeforeAsync(
        IReadOnlyCollection<string> addresses, DateTime before, int perAddress,
        CancellationToken ct = default);

    /// <summary>Counts telegrams in [from,to] (no other filter). Backs the statistics view.</summary>
    Task<int> CountInRangeAsync(DateTime from, DateTime to, CancellationToken ct = default);

    /// <summary>
    /// Streams (Timestamp only) for telegrams in [from,to] ordered ASC, for in-memory bucketing.
    /// The statistics endpoints aggregate in SQL instead (see <see cref="CountPerBucketAsync"/>);
    /// this stays as the reference the SQL aggregation is tested against.
    /// </summary>
    IAsyncEnumerable<DateTime> StreamTimestampsInRangeAsync(
        DateTime from, DateTime to, CancellationToken ct = default);

    /// <summary>
    /// Telegram counts in [from,to] split into <paramref name="bucketCount"/> buckets of
    /// <paramref name="bucketMs"/> milliseconds starting at <paramref name="from"/>, counted in
    /// SQL. A telegram lands in bucket <c>(int)((ts - from).TotalMilliseconds / bucketMs)</c>,
    /// clamped to the valid range, exactly like counting the streamed timestamps would.
    /// </summary>
    Task<long[]> CountPerBucketAsync(
        DateTime from, DateTime to, int bucketCount, double bucketMs, CancellationToken ct = default);

    /// <summary>Oldest and newest stored timestamp, or null when no telegram is stored.</summary>
    Task<(DateTime Oldest, DateTime Newest)?> GetTimestampBoundsAsync(CancellationToken ct = default);

    /// <summary>
    /// Telegram counts in [from,to] per weekday (0 = Sunday) and hour, counted in SQL after
    /// shifting each UTC timestamp by <paramref name="tzOffsetMinutes"/> (minutes the display zone
    /// is behind UTC, as JavaScript's getTimezoneOffset reports it). Returns a 7 x 24 grid.
    /// </summary>
    Task<long[][]> CountPerWeekdayHourAsync(
        DateTime from, DateTime to, int tzOffsetMinutes, CancellationToken ct = default);

    /// <summary>
    /// Destination group addresses with the most telegrams in [from,to], busiest first (ties by
    /// address). Grouped in SQL over the (Timestamp, DestinationAddress) index.
    /// </summary>
    Task<IReadOnlyList<AddressCount>> GetTopDestinationsAsync(
        DateTime from, DateTime to, int limit, CancellationToken ct = default);

    /// <summary>Source devices (individual addresses) with the most telegrams in [from,to], busiest first (ties by address).</summary>
    Task<IReadOnlyList<AddressCount>> GetTopSourcesAsync(
        DateTime from, DateTime to, int limit, CancellationToken ct = default);
}
