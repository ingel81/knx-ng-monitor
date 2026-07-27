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
    private const int DefaultStatsBuckets = 60;
    private const int MaxStatsBuckets = 500;

    /// <summary>
    /// Numeric time-series per destination group address. Each telegram's decoded value is
    /// projected to a number (leading numeric token, else On/Off-style boolean → 1/0); telegrams
    /// that yield no number are skipped. The result is down-sampled to at most maxPoints per address.
    /// </summary>
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

        var series = new List<ChartSeries>();
        foreach (var address in addressList)
        {
            if (!byAddress.TryGetValue(address, out var telegrams) || telegrams.Count == 0)
            {
                continue;
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
                    // Timestamps come back from SQLite as Kind=Unspecified but hold the UTC wall
                    // time (telegrams are saved with DateTime.UtcNow). ToUniversalTime() would
                    // then wrongly subtract the local offset; tag as UTC instead so the "O" format
                    // emits a correct trailing 'Z' and the client plots it at the right instant.
                    T = DateTime.SpecifyKind(t.Timestamp, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture),
                    V = value
                });
            }

            if (points.Count == 0)
            {
                continue;
            }

            var (sampled, downSampled) = DownSample(points, cap);

            series.Add(new ChartSeries
            {
                Address = address,
                Name = name,
                Unit = unit,
                DownSampled = downSampled,
                TotalPoints = points.Count,
                Points = sampled
            });
        }

        return Ok(new SeriesResponse { Series = series, Truncated = truncated });
    }

    /// <summary>
    /// Telegram volume histogram over [from,to] split into <paramref name="buckets"/> equal buckets,
    /// plus the total count. Backs the statistics "telegrams over time" / msg-per-second view.
    /// </summary>
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
        var counts = new long[bucketCount];
        long total = 0;

        await foreach (var ts in _repository.StreamTimestampsInRangeAsync(rangeFrom, rangeTo, ct))
        {
            total++;
            var offset = (ts - rangeFrom).TotalMilliseconds;
            var idx = (int)(offset / bucketMs);
            if (idx < 0) idx = 0;
            if (idx >= bucketCount) idx = bucketCount - 1;
            counts[idx]++;
        }

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
    [HttpGet("heatmap")]
    public async Task<IActionResult> GetHeatmap(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int tz = 0)
    {
        var (rangeFrom, rangeTo) = ResolveRange(from, to);
        var ct = HttpContext.RequestAborted;

        var grid = new long[7][];
        for (var d = 0; d < 7; d++) grid[d] = new long[24];
        long total = 0;

        await foreach (var ts in _repository.StreamTimestampsInRangeAsync(rangeFrom, rangeTo, ct))
        {
            // ts holds the UTC wall time (Kind=Unspecified from SQLite); shift into local zone.
            var local = DateTime.SpecifyKind(ts, DateTimeKind.Utc).AddMinutes(-tz);
            grid[(int)local.DayOfWeek][local.Hour]++;
            total++;
        }

        return Ok(new HeatmapResponse { Total = total, Grid = grid });
    }

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

    /// <summary>Uniform-stride down-sampling, keeping the first and last point. Returns whether it down-sampled.</summary>
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

public class HeatmapResponse
{
    public long Total { get; set; }
    /// <summary>Grid[weekday 0=Sun..6=Sat][hour 0..23] = telegram count.</summary>
    public long[][] Grid { get; set; } = System.Array.Empty<long[]>();
}
