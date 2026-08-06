using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using KnxMonitor.Core.DTOs;
using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Interfaces;
using KnxMonitor.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnxMonitor.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public partial class TelegramsController : ControllerBase
{
    private const int MaxPageSize = 1000;
    private const int DefaultPageSize = 100;

    private readonly ITelegramRepository _repository;
    private readonly TelegramArchiveService _archive;
    private readonly ILogger<TelegramsController> _logger;

    public TelegramsController(
        ITelegramRepository repository,
        TelegramArchiveService archive,
        ILogger<TelegramsController> logger)
    {
        _repository = repository;
        _archive = archive;
        _logger = logger;
    }

    /// <summary>Keyset-paginated telegram history (newest first).</summary>
    /// <remarks>
    /// Reads the database hot tier only — the day archive on disk is served by the
    /// <c>archive/*</c> endpoints. Filters: <c>from</c>/<c>to</c>, <c>address</c> (destination
    /// group address), <c>source</c> (individual address), <c>type</c> or <c>types</c>
    /// (comma-separated, OR-combined and taking precedence over <c>type</c>) and <c>q</c>
    /// (free-text LIKE across source, destination, decoded value, GA name and DPT).
    /// <para>
    /// Paging is keyset-based over (Timestamp, Id), not offset-based: pass the <c>nextCursor</c>
    /// of the previous response as <c>cursor</c> to get the following page. The cursor is opaque;
    /// a malformed one yields 400. <c>pageSize</c> defaults to 100 and is clamped to 1..1000.
    /// <c>order=asc</c> sorts oldest first, any other value newest first — the sort direction must
    /// stay the same while paging, because the cursor is interpreted relative to it.
    /// </para>
    /// <para>
    /// <c>from</c>/<c>to</c> are compared against the stored UTC timestamps without any conversion
    /// (unlike the chart endpoints), so pass UTC values.
    /// </para>
    /// </remarks>
    /// <returns>
    /// One page of telegrams plus <c>hasMore</c> and <c>nextCursor</c>; <c>nextCursor</c> is null
    /// on the last page.
    /// </returns>
    [HttpGet]
    public async Task<IActionResult> GetHistory([FromQuery] TelegramQueryRequest request)
    {
        var pageSize = Math.Clamp(request.PageSize <= 0 ? DefaultPageSize : request.PageSize, 1, MaxPageSize);

        DateTime? cursorTs = null;
        int? cursorId = null;
        if (!string.IsNullOrWhiteSpace(request.Cursor))
        {
            if (!TryDecodeCursor(request.Cursor, out var ts, out var id))
            {
                return BadRequest(new { Message = "Invalid cursor" });
            }
            cursorTs = ts;
            cursorId = id;
        }

        var (items, hasMore) = await _repository.GetPageKeysetAsync(request.ToFilter(), cursorTs, cursorId, pageSize);
        var dtos = items.Select(ToDto).ToList();

        string? nextCursor = null;
        if (hasMore && items.Count > 0)
        {
            var last = items[^1];
            nextCursor = EncodeCursor(last.Timestamp, last.Id);
        }

        return Ok(new PagedResult<TelegramDto>
        {
            Items = dtos,
            NextCursor = nextCursor,
            HasMore = hasMore
        });
    }

    /// <summary>Number of telegrams matching a filter.</summary>
    /// <remarks>
    /// Takes the same query parameters as <c>GET /api/telegrams</c>, but only the filtering ones
    /// have an effect — <c>cursor</c>, <c>pageSize</c> and <c>order</c> are ignored. Counts the
    /// database hot tier only.
    /// </remarks>
    /// <returns>An object with a single <c>count</c> property.</returns>
    [HttpGet("count")]
    public async Task<IActionResult> GetCount([FromQuery] TelegramQueryRequest request)
    {
        var count = await _repository.CountByFilterAsync(request.ToFilter());
        return Ok(new { count });
    }

    /// <summary>Permanently deletes the entire telegram history (the DB hot-tier). Irreversible.</summary>
    /// <remarks>
    /// Deletes every row in one statement — the request carries no filter, so there is no way to
    /// clear only part of the history. The SQLite WAL is checkpointed afterwards so the file
    /// shrinks. Archive files on disk are not touched and stay downloadable.
    /// </remarks>
    /// <returns>An object with a single <c>deleted</c> property: the number of rows removed.</returns>
    [HttpDelete]
    public async Task<IActionResult> ClearAll()
    {
        var deleted = await _repository.DeleteAllAsync();
        _logger.LogWarning("Telegram history cleared by user: {Deleted} rows deleted", deleted);
        return Ok(new { deleted });
    }

    /// <summary>Server-streamed CSV export over the full filtered DB range.</summary>
    /// <remarks>
    /// Accepts the same filters as <c>GET /api/telegrams</c>, but exports every matching row:
    /// <c>cursor</c> and <c>pageSize</c> are ignored and there is no row limit. Rows are always
    /// written chronologically (oldest first), regardless of <c>order</c>.
    /// <para>
    /// The response is streamed while the query is still running, so the body starts arriving
    /// before the row count is known and no <c>Content-Length</c> is sent. The header line is
    /// always written, even when nothing matches. Encoding is UTF-8 without BOM; fields that
    /// contain a comma, quote or line break are quoted with doubled inner quotes. The filename is
    /// <c>knx-telegrams-{UTC timestamp}.csv</c>.
    /// </para>
    /// <para>
    /// Columns: Timestamp, SourceAddress, DestinationAddress, GroupAddressName, DatapointType,
    /// MessageType, Value, ValueDecoded. <c>Value</c> is the raw payload as uppercase hex without
    /// separators. <c>Timestamp</c> is the UTC value in ISO-8601 round-trip format but carries no
    /// zone designator — unlike the JSON responses, which end in <c>Z</c>. GroupAddressName and
    /// DatapointType are empty when the telegram has no linked group address.
    /// </para>
    /// </remarks>
    /// <param name="request">Same filters as the history query; see the remarks above.</param>
    /// <param name="format">Only <c>csv</c> is supported; any other value yields 400.</param>
    /// <returns>A streamed <c>text/csv</c> attachment.</returns>
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] TelegramQueryRequest request, string format = "csv")
    {
        if (!string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { Message = "Only format=csv is supported" });
        }

        var ct = HttpContext.RequestAborted;
        Response.ContentType = "text/csv; charset=utf-8";
        Response.Headers.ContentDisposition =
            $"attachment; filename=\"knx-telegrams-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv\"";

        await using var writer = new StreamWriter(Response.Body, new UTF8Encoding(false));
        await writer.WriteLineAsync(
            "Timestamp,SourceAddress,DestinationAddress,GroupAddressName,DatapointType,MessageType,Value,ValueDecoded");

        await foreach (var t in _repository.StreamByFilterAsync(request.ToFilter(), ct))
        {
            var line = string.Join(',',
                Csv(t.Timestamp.ToString("O", CultureInfo.InvariantCulture)),
                Csv(t.SourceAddress),
                Csv(t.DestinationAddress),
                Csv(t.GroupAddress?.Name),
                Csv(t.GroupAddress?.DatapointType),
                Csv(t.MessageType.ToString()),
                Csv(Convert.ToHexString(t.Value)),
                Csv(t.ValueDecoded));
            await writer.WriteLineAsync(line);
        }

        await writer.FlushAsync(ct);
        return new EmptyResult();
    }

    /// <summary>Lists the days available in the cold-tier archive, newest first.</summary>
    /// <remarks>
    /// The archive holds one NDJSON file per UTC day, written independently of the database hot
    /// tier and unaffected by its retention or by <c>DELETE /api/telegrams</c>. Finished days are
    /// gzipped (<c>compressed: true</c>), the current day stays plain and is still being appended
    /// to, so its size grows between calls. <c>sizeBytes</c> is the size on disk, i.e. the
    /// compressed size for gzipped days. Archiving is opt-in via the recording settings, so the
    /// list is empty as long as nothing has been archived.
    /// </remarks>
    /// <returns>One entry per day with <c>date</c> (YYYY-MM-DD), <c>fileName</c>, <c>sizeBytes</c> and <c>compressed</c>.</returns>
    [HttpGet("archive/days")]
    public IActionResult GetArchiveDays()
    {
        return Ok(_archive.GetArchiveDays());
    }

    /// <summary>Downloads the raw archive file of one UTC day.</summary>
    /// <remarks>
    /// Serves the gzipped file (<c>application/gzip</c>) when it exists and falls back to the
    /// plain, still-open file of the current day (<c>application/x-ndjson</c>); 404 when neither
    /// is present for that day. The payload is NDJSON — one JSON object per line with the keys
    /// <c>t</c> (UTC timestamp), <c>s</c> (source address), <c>d</c> (destination address),
    /// <c>mt</c> (message type), <c>r</c> (raw value) and <c>v</c> (decoded value, omitted when
    /// there is none). The current day is served while the writer still holds it open, so it can
    /// be missing the last few seconds — the writer flushes on an interval.
    /// </remarks>
    /// <param name="date">Archive day as <c>YYYY-MM-DD</c> (UTC). Any other shape yields 400.</param>
    [HttpGet("archive/{date}")]
    public IActionResult DownloadArchiveDay(string date)
    {
        if (!DatePattern().IsMatch(date))
        {
            return BadRequest(new { Message = "date must be YYYY-MM-DD" });
        }

        var opened = _archive.OpenArchiveDay(date);
        if (opened is null)
        {
            return NotFound();
        }

        var (stream, fileName, contentType) = opened.Value;
        return File(stream, contentType, fileName);
    }

    private static TelegramDto ToDto(KnxTelegram t) => new()
    {
        Id = t.Id,
        // SQLite returns Kind=Unspecified though the value is UTC (saved via DateTime.UtcNow).
        // Tag it UTC so JSON serializes with a trailing 'Z' and the client renders local time
        // correctly (otherwise the archive grid sits an offset behind the live view).
        Timestamp = DateTime.SpecifyKind(t.Timestamp, DateTimeKind.Utc),
        SourceAddress = t.SourceAddress,
        DestinationAddress = t.DestinationAddress,
        GroupAddressName = t.GroupAddress?.Name,
        DatapointType = t.GroupAddress?.DatapointType,
        MessageType = t.MessageType,
        Value = Convert.ToHexString(t.Value),
        ValueDecoded = t.ValueDecoded,
        Priority = t.Priority,
        Flags = t.Flags
    };

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        return value;
    }

    private static string EncodeCursor(DateTime timestamp, int id)
    {
        var raw = timestamp.ToString("O", CultureInfo.InvariantCulture) + "|" + id.ToString(CultureInfo.InvariantCulture);
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
        return b64.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static bool TryDecodeCursor(string cursor, out DateTime timestamp, out int id)
    {
        timestamp = default;
        id = 0;
        try
        {
            var b64 = cursor.Replace('-', '+').Replace('_', '/');
            switch (b64.Length % 4)
            {
                case 2: b64 += "=="; break;
                case 3: b64 += "="; break;
            }
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
            var sep = raw.LastIndexOf('|');
            if (sep <= 0)
            {
                return false;
            }
            timestamp = DateTime.ParseExact(raw[..sep], "O", CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind);
            return int.TryParse(raw[(sep + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out id);
        }
        catch
        {
            return false;
        }
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$")]
    private static partial Regex DatePattern();
}
