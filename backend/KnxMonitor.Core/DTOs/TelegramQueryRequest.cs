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

    /// <summary>Comma-separated message types for multi-select (e.g. "Write,Response").</summary>
    public string? Types { get; set; }

    /// <summary>Free-text search across source/destination, GA name, DPT and decoded value.</summary>
    public string? Q { get; set; }

    /// <summary>Sort direction by timestamp: "asc" = oldest first; anything else = newest first.</summary>
    public string? Order { get; set; }

    /// <summary>Opaque keyset cursor (base64url of "{Timestamp:O}|{Id}"). Null = first page.</summary>
    public string? Cursor { get; set; }

    public int PageSize { get; set; } = 100;

    public TelegramFilter ToFilter() => new()
    {
        From = From,
        To = To,
        Address = string.IsNullOrWhiteSpace(Address) ? null : Address,
        Source = string.IsNullOrWhiteSpace(Source) ? null : Source,
        Type = Type,
        Types = ParseTypes(Types),
        Q = string.IsNullOrWhiteSpace(Q) ? null : Q,
        Ascending = string.Equals(Order, "asc", StringComparison.OrdinalIgnoreCase)
    };

    private static List<MessageType>? ParseTypes(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return null;
        var list = new List<MessageType>();
        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<MessageType>(part, ignoreCase: true, out var mt) && !list.Contains(mt))
                list.Add(mt);
        }
        return list.Count > 0 ? list : null;
    }
}
