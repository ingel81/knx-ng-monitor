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

    [HttpGet("count")]
    public async Task<IActionResult> GetCount([FromQuery] TelegramQueryRequest request)
    {
        var count = await _repository.CountByFilterAsync(request.ToFilter());
        return Ok(new { count });
    }

    /// <summary>Permanently deletes the entire telegram history (the DB hot-tier). Irreversible.</summary>
    [HttpDelete]
    public async Task<IActionResult> ClearAll()
    {
        var deleted = await _repository.DeleteAllAsync();
        _logger.LogWarning("Telegram history cleared by user: {Deleted} rows deleted", deleted);
        return Ok(new { deleted });
    }

    /// <summary>Server-streamed CSV export over the full filtered DB range.</summary>
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

    [HttpGet("archive/days")]
    public IActionResult GetArchiveDays()
    {
        return Ok(_archive.GetArchiveDays());
    }

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
        Timestamp = t.Timestamp,
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
