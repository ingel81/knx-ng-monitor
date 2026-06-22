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
        var rows = await _repository.GetSeriesRangeAsync(addressList, rangeFrom, rangeTo, SeriesRowCap, ct);

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
                Points = sampled
            });
        }

        return Ok(new SeriesResponse { Series = series });
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
    private static (List<ChartPoint> Points, bool DownSampled) DownSample(List<ChartPoint> points, int maxPoints)
    {
        if (points.Count <= maxPoints)
        {
            return (points, false);
        }

        // maxPoints is clamped to >= 1 by the caller. With exactly 1 the stride formula below
        // would divide by zero, so collapse to the single most-recent point.
        if (maxPoints <= 1)
        {
            return (new List<ChartPoint> { points[^1] }, true);
        }

        var result = new List<ChartPoint>(maxPoints);
        // Stride so we land on at most maxPoints points, always including first and last.
        var stride = (double)(points.Count - 1) / (maxPoints - 1);
        for (var i = 0; i < maxPoints; i++)
        {
            var idx = (int)Math.Round(i * stride);
            if (idx >= points.Count) idx = points.Count - 1;
            result.Add(points[idx]);
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
}

public class ChartSeries
{
    public string Address { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string Unit { get; set; } = string.Empty;
    public bool DownSampled { get; set; }
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
