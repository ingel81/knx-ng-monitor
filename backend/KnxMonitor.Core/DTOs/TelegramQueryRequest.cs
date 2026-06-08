using KnxMonitor.Core.Enums;

namespace KnxMonitor.Core.DTOs;

/// <summary>Bound from the query string of GET /api/telegrams and /count.</summary>
public class TelegramQueryRequest
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string? Address { get; set; }
    public string? Source { get; set; }
    public MessageType? Type { get; set; }

    /// <summary>Opaque keyset cursor (base64url of "{Timestamp:O}|{Id}"). Null = first page.</summary>
    public string? Cursor { get; set; }

    public int PageSize { get; set; } = 100;

    public TelegramFilter ToFilter() => new()
    {
        From = From,
        To = To,
        Address = string.IsNullOrWhiteSpace(Address) ? null : Address,
        Source = string.IsNullOrWhiteSpace(Source) ? null : Source,
        Type = Type
    };
}
