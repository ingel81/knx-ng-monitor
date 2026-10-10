using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;

namespace KnxMonitor.Api.Controllers;

/// <summary>
/// Charts &amp; statistics endpoints (time-series + telegram histogram). Lives on the
/// telegram route family since it reads the same hot-tier table.
/// </summary>
public partial class TelegramsController
{
    private const int MaxSeriesAddresses = 8;
    private const int DefaultMaxPoints = 2000;
    private const int MaxMaxPoints = 10000;
    // Hard cap on rows pulled per series request so a wide range stays bounded.
    private const int SeriesRowCap = 200_000;
    // Earlier telegrams looked at per address when searching the carry-in value: the newest one
    // may carry no number (a read request), so a few are needed, but not the whole history.
    private const int CarryLookback = 20;
    private const int DefaultStatsBuckets = 60;
    private const int MaxStatsBuckets = 500;
    private const int DefaultTopLimit = 20;
    private const int MaxTopLimit = 100;

    /// <summary>
    /// Numeric time-series per destination group address. Each telegram's decoded value is
    /// projected to a number (leading numeric token, else On/Off-style boolean → 1/0); telegrams
    /// that yield no number are skipped. The result is down-sampled to at most maxPoints per address.
    /// </summary>
    /// <remarks>
    /// A number is taken from the decoded value as the first numeric token (leading minus and
    /// decimal point or comma allowed), so "21.5 °C" becomes 21.5; failing that, a known
    /// boolean-ish word (On/Off, True/False, Open/Closed, Up/Down, …) becomes 1 or 0. Anything
    /// else is dropped, and an address whose telegrams yield no number at all is omitted from the
    /// response instead of appearing empty. <c>unit</c> is whatever remains of the first chartable
    /// sample once the number is stripped — "°C" for "21.5 °C"; for a boolean sample, which holds
    /// no number to strip, the word itself remains.
    /// <para>
    /// Down-sampling keeps the minimum and the maximum of each bucket rather than every n-th
    /// point, so spikes survive a wide range. <c>maxPoints</c> is the budget per address, not the
    /// exact count — the result may be shorter, and <c>downSampled</c> plus <c>totalPoints</c>
    /// (the numeric points before down-sampling) report what happened.
    /// </para>
    /// <para>
    /// At most 200 000 rows are read per request. When the range holds more, the <b>newest</b>
    /// rows win, the older end is missing and <c>truncated</c> is true — a chart is easier to
    /// distrust when it starts late than when it ends early.
    /// </para>
    /// <para>
    /// <c>carry</c> is the last chartable value <b>before</b> <c>from</c> (with its original
    /// timestamp), so a client can draw the curve from the left edge instead of starting it at
    /// the first telegram inside the range, since a KNX value holds until the next telegram. It is null
    /// when there is no earlier value, and always null when <c>truncated</c> is true, since the
    /// missing older end would sit between it and the first point. An address with a carry value
    /// but no telegram inside the range is returned with an empty <c>points</c> list.
    /// </para>
    /// <para>
    /// A timestamp without a zone designator (<c>from=2026-07-30T00:00:00</c>) is interpreted as
    /// server local time, not UTC. Send ISO-8601 with a trailing <c>Z</c> to be unambiguous.
    /// Returned point timestamps are always UTC with a trailing <c>Z</c>.
    /// </para>
    /// </remarks>
    /// <param name="addresses">
    /// Comma-separated destination group addresses (e.g. <c>1/2/3,1/2/4</c>). Duplicates are
    /// dropped and at most the first 8 are used; the rest are silently ignored. Missing or empty
    /// yields an empty series list rather than an error.
    /// </param>
    /// <param name="from">Start of the range (inclusive). Defaults to 24 hours before <c>to</c>.</param>
    /// <param name="to">End of the range (inclusive). Defaults to now. A reversed range is swapped, not rejected.</param>
    /// <param name="maxPoints">Maximum points returned per address after down-sampling. Default 2000, clamped to 1..10000.</param>
    /// <returns>
    /// One entry per requested address that has chartable data, in the order the addresses were
    /// requested, plus a <c>truncated</c> flag for the whole request.
    /// </returns>
    [HttpGet("series")]
    public async Task<IActionResult> GetSeries(
        [FromQuery] string? addresses,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int maxPoints = DefaultMaxPoints)
    {
        var addressList = (addresses ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .Take(MaxSeriesAddresses)
            .ToList();

        if (addressList.Count == 0)
        {
            return Ok(new SeriesResponse { Series = new List<ChartSeries>() });
        }

        var (rangeFrom, rangeTo) = ResolveRange(from, to);
        var cap = Math.Clamp(maxPoints, 1, MaxMaxPoints);

        var ct = HttpContext.RequestAborted;
        var (rows, truncated) = await _repository.GetSeriesRangeAsync(addressList, rangeFrom, rangeTo, SeriesRowCap, ct);

        // Group by destination address, preserving the requested order.
        var byAddress = rows
            .GroupBy(t => t.DestinationAddress, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        // Skipped when truncated: the carry value would then bridge the part that is missing.
        var earlier = truncated
            ? new Dictionary<string, IReadOnlyList<KnxMonitor.Core.Entities.KnxTelegram>>()
            : await _repository.GetLatestBeforeAsync(addressList, rangeFrom, CarryLookback, ct);

        var series = new List<ChartSeries>();
        foreach (var address in addressList)
        {
            byAddress.TryGetValue(address, out var telegrams);
            telegrams ??= new List<KnxMonitor.Core.Entities.KnxTelegram>();

            ChartPoint? carry = null;
            string carryUnit = string.Empty;
            string? carryName = null;
            if (earlier.TryGetValue(address, out var before))
            {
                foreach (var t in before)
                {
                    if (TryExtractNumeric(t.ValueDecoded, out var carryValue))
                    {
                        carry = new ChartPoint { T = ToIsoUtc(t.Timestamp), V = carryValue };
                        carryUnit = ExtractUnit(t.ValueDecoded);
                        carryName = t.GroupAddress?.Name;
                        break;
                    }
                }
            }

            string? name = null;
            string unit = string.Empty;
            var points = new List<ChartPoint>(telegrams.Count);

            foreach (var t in telegrams)
            {
                name ??= t.GroupAddress?.Name;
                if (!TryExtractNumeric(t.ValueDecoded, out var value))
                {
                    continue;
                }
                if (unit.Length == 0)
                {
                    unit = ExtractUnit(t.ValueDecoded);
                }
                points.Add(new ChartPoint
                {
                    T = ToIsoUtc(t.Timestamp),
                    V = value
                });
            }

            if (points.Count == 0 && carry is null)
            {
                continue;
            }

            var (sampled, downSampled) = DownSample(points, cap);

            series.Add(new ChartSeries
            {
                Address = address,
                Name = name ?? carryName,
                Unit = unit.Length > 0 ? unit : carryUnit,
                DownSampled = downSampled,
                TotalPoints = points.Count,
                Points = sampled,
                Carry = carry
            });
        }

        return Ok(new SeriesResponse { Series = series, Truncated = truncated });
    }

    /// <summary>
    /// Telegram volume histogram over [from,to] split into <paramref name="buckets"/> equal buckets,
    /// plus the total count. Backs the statistics "telegrams over time" / msg-per-second view.
    /// </summary>
    /// <remarks>
    /// Counts all telegrams in the range — there is no address, source or type filter here. Every
    /// bucket is returned, including empty ones, so the buckets always tile the range gap-free.
    /// <c>bucketMs</c> is the bucket width in milliseconds and is fractional: the range is divided
    /// into equal parts rather than snapped to a round interval, and each bucket's <c>t</c> is its
    /// start. Divide a count by <c>bucketMs</c> to get a rate.
    /// <para>
    /// A timestamp without a zone designator is interpreted as server local time, not UTC. Send
    /// ISO-8601 with a trailing <c>Z</c> to be unambiguous.
    /// </para>
    /// </remarks>
    /// <param name="from">Start of the range (inclusive). Defaults to 24 hours before <c>to</c>.</param>
    /// <param name="to">End of the range (inclusive). Defaults to now. A reversed range is swapped, not rejected.</param>
    /// <param name="buckets">Number of equal-width buckets. Default 60, clamped to 1..500.</param>
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int buckets = DefaultStatsBuckets)
    {
        var (rangeFrom, rangeTo) = ResolveRange(from, to);
        var bucketCount = Math.Clamp(buckets, 1, MaxStatsBuckets);

        var totalMs = (rangeTo - rangeFrom).TotalMilliseconds;
        if (totalMs <= 0)
        {
            totalMs = 1;
        }
        var bucketMs = totalMs / bucketCount;

        var ct = HttpContext.RequestAborted;
        // Counted per bucket in SQL (#39): only the bucket counts cross into .NET instead of one
        // timestamp per telegram, which took seconds for a 30-day range on a Pi.
        var counts = await _repository.CountPerBucketAsync(rangeFrom, rangeTo, bucketCount, bucketMs, ct);
        var total = counts.Sum();

        var bucketList = new List<StatsBucket>(bucketCount);
        for (var i = 0; i < bucketCount; i++)
        {
            var bucketStart = rangeFrom.AddMilliseconds(bucketMs * i);
            bucketList.Add(new StatsBucket
            {
                T = bucketStart.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                Count = counts[i]
            });
        }

        return Ok(new StatsResponse
        {
            Total = total,
            BucketMs = bucketMs,
            Counts = bucketList
        });
    }

    /// <summary>
    /// Activity heatmap: telegram counts bucketed by (weekday, hour-of-day) over [from,to].
    /// Binning is done in the caller's local time via <paramref name="tz"/> (minutes the local
    /// zone is behind UTC, i.e. JS getTimezoneOffset()), so "18:00 Monday" means the user's 18:00.
    /// </summary>
    /// <remarks>
    /// The grid is always 7 × 24 (weekday 0 = Sunday .. 6 = Saturday, then hour 0..23) and is
    /// filled with counts, not averages: a 30-day range puts roughly four Mondays into the same
    /// cell. Counts all telegrams in the range — there is no address, source or type filter.
    /// <para>
    /// A timestamp without a zone designator is interpreted as server local time, not UTC. Send
    /// ISO-8601 with a trailing <c>Z</c> to be unambiguous. Note that <c>tz</c> shifts only the
    /// binning, not the range boundaries.
    /// </para>
    /// </remarks>
    /// <param name="from">Start of the range (inclusive). Defaults to 24 hours before <c>to</c>.</param>
    /// <param name="to">End of the range (inclusive). Defaults to now. A reversed range is swapped, not rejected.</param>
    /// <param name="tz">
    /// Minutes the display zone is behind UTC, exactly as JavaScript's
    /// <c>Date.getTimezoneOffset()</c> reports it (CEST = -120). Default 0 bins in UTC. A fixed
    /// offset is applied to the whole range, so a range spanning a DST change is binned with one
    /// offset throughout.
    /// </param>
    [HttpGet("heatmap")]
    public async Task<IActionResult> GetHeatmap(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int tz = 0)
    {
        var (rangeFrom, rangeTo) = ResolveRange(from, to);
        var ct = HttpContext.RequestAborted;

        // The SQL counts one range per local hour, so its cost grows with the length of the range,
        // not with the data. Clamp the range to what is stored (a typo like year 0202 would
        // otherwise build millions of ranges) and the offset to real time zones (UTC-14..+14).
        tz = Math.Clamp(tz, -840, 840);
        var bounds = await _repository.GetTimestampBoundsAsync(ct);
        if (bounds is not { } b || rangeTo < b.Oldest || rangeFrom > b.Newest)
        {
            return Ok(new HeatmapResponse { Total = 0, Grid = EmptyHeatmapGrid() });
        }
        if (rangeFrom < b.Oldest) rangeFrom = b.Oldest;
        if (rangeTo > b.Newest) rangeTo = b.Newest;

        // Counted per (weekday, hour) in SQL (#39): at most 168 rows instead of every timestamp.
        var grid = await _repository.CountPerWeekdayHourAsync(rangeFrom, rangeTo, tz, ct);
        var total = grid.Sum(row => row.Sum());

        return Ok(new HeatmapResponse { Total = total, Grid = grid });
    }

    /// <summary>
    /// The busiest destination group addresses and the busiest senders over [from,to], for the
    /// bus-load breakdown in the statistics view.
    /// </summary>
    /// <remarks>
    /// Both lists are ranked by telegram count, highest first, ties broken by address, and hold
    /// at most <paramref name="limit"/> entries each. <c>share</c> is the percentage of all
    /// telegrams in the range (<c>total</c>), <c>perHour</c> the count divided by the length of
    /// the range in hours (<c>rangeHours</c>), so a short range is not mistaken for a quiet one.
    /// <para>
    /// Names and DPTs come from the <b>currently active</b> project: an address that is not part
    /// of it, or a range recorded under an older project, shows whatever today's project says,
    /// or null. Counts all telegrams in the range, every message type included.
    /// </para>
    /// <para>
    /// A timestamp without a zone designator is interpreted as server local time, not UTC. Send
    /// ISO-8601 with a trailing <c>Z</c> to be unambiguous.
    /// </para>
    /// </remarks>
    /// <param name="from">Start of the range (inclusive). Defaults to 24 hours before <c>to</c>.</param>
    /// <param name="to">End of the range (inclusive). Defaults to now. A reversed range is swapped, not rejected.</param>
    /// <param name="limit">Entries per list. Default 20, clamped to 1..100.</param>
    [HttpGet("top")]
    public async Task<IActionResult> GetTop(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int limit = DefaultTopLimit)
    {
        var (rangeFrom, rangeTo) = ResolveRange(from, to);
        var take = Math.Clamp(limit, 1, MaxTopLimit);
        var ct = HttpContext.RequestAborted;

        // Sequential on purpose: the repository shares one DbContext, which allows a single
        // active query at a time.
        var total = await _repository.CountInRangeAsync(rangeFrom, rangeTo, ct);
        var destinations = await _repository.GetTopDestinationsAsync(rangeFrom, rangeTo, take, ct);
        var sources = await _repository.GetTopSourcesAsync(rangeFrom, rangeTo, take, ct);

        var hours = Math.Max((rangeTo - rangeFrom).TotalHours, 1.0 / 3600);
        double Share(long count) => total > 0 ? count * 100.0 / total : 0;

        return Ok(new TopResponse
        {
            Total = total,
            RangeHours = hours,
            GroupAddresses = destinations.Select(d =>
            {
                var ga = _projectCache.GetByAddress(d.Address);
                return new TopEntry
                {
                    Address = d.Address,
                    Name = string.IsNullOrEmpty(ga?.Name) ? null : ga.Name,
                    Dpt = ga?.DatapointType,
                    Count = d.Count,
                    Share = Share(d.Count),
                    PerHour = d.Count / hours
                };
            }).ToList(),
            Senders = sources.Select(s =>
            {
                var device = _projectCache.GetDeviceByAddress(s.Address);
                return new TopEntry
                {
                    Address = s.Address,
                    Name = string.IsNullOrEmpty(device?.Name) ? null : device.Name,
                    Count = s.Count,
                    Share = Share(s.Count),
                    PerHour = s.Count / hours
                };
            }).ToList()
        });
    }

    private static long[][] EmptyHeatmapGrid() =>
        Enumerable.Range(0, 7).Select(_ => new long[24]).ToArray();

    private static (DateTime From, DateTime To) ResolveRange(DateTime? from, DateTime? to)
    {
        var rangeTo = (to ?? DateTime.UtcNow).ToUniversalTime();
        var rangeFrom = (from ?? rangeTo.AddHours(-24)).ToUniversalTime();
        if (rangeFrom > rangeTo)
        {
            (rangeFrom, rangeTo) = (rangeTo, rangeFrom);
        }
        return (rangeFrom, rangeTo);
    }

    /// <summary>
    /// Reduces a series to at most <paramref name="maxPoints"/> points by keeping the minimum and
    /// the maximum of each bucket.
    /// <para>
    /// This used to take every n-th point, which is cheap but drops extremes: a short temperature
    /// spike inside a 30-day view disappeared entirely and left a smooth, trustworthy-looking
    /// curve behind. Keeping both extremes per bucket preserves the envelope of the signal — for
    /// a monitoring tool the peaks are usually the interesting part.
    /// </para>
    /// </summary>
    private static (List<ChartPoint> Points, bool DownSampled) DownSample(List<ChartPoint> points, int maxPoints)
    {
        if (points.Count <= maxPoints)
        {
            return (points, false);
        }

        // Two points per bucket, so halve the budget. maxPoints is clamped to >= 1 by the caller;
        // with 1 or 2 there is no room for a range, so fall back to the most recent point.
        var buckets = maxPoints / 2;
        if (buckets < 2)
        {
            return (new List<ChartPoint> { points[^1] }, true);
        }

        var result = new List<ChartPoint>(maxPoints);
        var bucketSize = (double)points.Count / buckets;

        for (var b = 0; b < buckets; b++)
        {
            var start = (int)Math.Floor(b * bucketSize);
            var end = b == buckets - 1 ? points.Count : (int)Math.Floor((b + 1) * bucketSize);
            if (end <= start)
            {
                continue;
            }

            var minIdx = start;
            var maxIdx = start;
            for (var i = start + 1; i < end; i++)
            {
                if (points[i].V < points[minIdx].V) minIdx = i;
                if (points[i].V > points[maxIdx].V) maxIdx = i;
            }

            if (minIdx == maxIdx)
            {
                result.Add(points[minIdx]);
                continue;
            }

            // Emit in chronological order — the series must stay monotonic in time.
            var (firstIdx, secondIdx) = minIdx < maxIdx ? (minIdx, maxIdx) : (maxIdx, minIdx);
            result.Add(points[firstIdx]);
            result.Add(points[secondIdx]);
        }

        return (result, true);
    }

    // --- Numeric extraction (mirrored in the frontend for live append) ---

    private static readonly Regex NumericToken =
        new(@"-?\d+(?:[.,]\d+)?", RegexOptions.Compiled);

    private static readonly HashSet<string> TruthyTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "On", "True", "Active", "Open", "Yes", "Up", "Start", "Enable"
    };

    private static readonly HashSet<string> FalsyTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "Off", "False", "Inactive", "Close", "Closed", "No", "Down", "Stop", "Disable"
    };

    /// <summary>
    /// Extracts a chartable number from a decoded value: the first numeric token (allowing a
    /// leading minus and a decimal point/comma), else a known boolean/enum word → 1/0. Returns
    /// false when nothing chartable is present.
    /// </summary>
    // Timestamps come back from SQLite as Kind=Unspecified but hold the UTC wall time (telegrams
    // are saved with DateTime.UtcNow). ToUniversalTime() would then wrongly subtract the local
    // offset; tag as UTC instead so the "O" format emits a correct trailing 'Z' and the client
    // plots it at the right instant.
    private static string ToIsoUtc(DateTime timestamp) =>
        DateTime.SpecifyKind(timestamp, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);

    internal static bool TryExtractNumeric(string? decoded, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(decoded))
        {
            return false;
        }

        var match = NumericToken.Match(decoded);
        if (match.Success)
        {
            var token = match.Value.Replace(',', '.');
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }
        }

        var trimmed = decoded.Trim();
        if (TruthyTokens.Contains(trimmed))
        {
            value = 1;
            return true;
        }
        if (FalsyTokens.Contains(trimmed))
        {
            value = 0;
            return true;
        }

        return false;
    }

    /// <summary>Derives the unit by stripping the numeric token (and surrounding whitespace) from a sample.</summary>
    internal static string ExtractUnit(string? decoded)
    {
        if (string.IsNullOrWhiteSpace(decoded))
        {
            return string.Empty;
        }
        var stripped = NumericToken.Replace(decoded, string.Empty, 1).Trim();
        return stripped;
    }
}

// --- Response DTOs ---

public class SeriesResponse
{
    public List<ChartSeries> Series { get; set; } = new();

    /// <summary>
    /// True when the row cap was reached and the requested range could not be read in full.
    /// The returned data is the most recent part of it; the older end is missing.
    /// </summary>
    public bool Truncated { get; set; }
}

public class ChartSeries
{
    public string Address { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string Unit { get; set; } = string.Empty;
    public bool DownSampled { get; set; }

    /// <summary>Numeric points available before down-sampling — lets the UI state the real ratio.</summary>
    public int TotalPoints { get; set; }

    public List<ChartPoint> Points { get; set; } = new();

    /// <summary>
    /// Last chartable value before the range start, with its original timestamp; null when there
    /// is none or the result is truncated. Not part of <see cref="Points"/> or <see cref="TotalPoints"/>.
    /// </summary>
    public ChartPoint? Carry { get; set; }
}

public class ChartPoint
{
    public string T { get; set; } = string.Empty;
    public double V { get; set; }
}

public class StatsResponse
{
    public long Total { get; set; }
    public double BucketMs { get; set; }
    public List<StatsBucket> Counts { get; set; } = new();
}

public class StatsBucket
{
    public string T { get; set; } = string.Empty;
    public long Count { get; set; }
}

public class TopResponse
{
    /// <summary>All telegrams in the range, the base of every <c>share</c>.</summary>
    public long Total { get; set; }

    /// <summary>Length of the range in hours, the base of every <c>perHour</c>.</summary>
    public double RangeHours { get; set; }

    /// <summary>Busiest destination group addresses, highest count first.</summary>
    public List<TopEntry> GroupAddresses { get; set; } = new();

    /// <summary>Busiest source devices (individual addresses), highest count first.</summary>
    public List<TopEntry> Senders { get; set; } = new();
}

public class TopEntry
{
    /// <summary>Group address (<c>1/2/3</c>) or individual address (<c>1.1.5</c>).</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>GA or device name from the active project; null when it has none.</summary>
    public string? Name { get; set; }

    /// <summary>Datapoint type of the GA from the active project; always null for senders.</summary>
    public string? Dpt { get; set; }

    public long Count { get; set; }

    /// <summary>Percentage of all telegrams in the range, 0..100.</summary>
    public double Share { get; set; }

    /// <summary>Average telegrams per hour over the whole range.</summary>
    public double PerHour { get; set; }
}

public class HeatmapResponse
{
    public long Total { get; set; }
    /// <summary>Grid[weekday 0=Sun..6=Sat][hour 0..23] = telegram count.</summary>
    public long[][] Grid { get; set; } = System.Array.Empty<long[]>();
}
