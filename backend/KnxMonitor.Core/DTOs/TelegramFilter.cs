using KnxMonitor.Core.Enums;

namespace KnxMonitor.Core.DTOs;

/// <summary>Filter applied to telegram history queries (time range + address/source/type).</summary>
public class TelegramFilter
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    /// <summary>Destination group address (e.g. "1/2/3").</summary>
    public string? Address { get; set; }

    /// <summary>Source individual address (e.g. "1.1.5").</summary>
    public string? Source { get; set; }

    public MessageType? Type { get; set; }

    /// <summary>Multi-select message types (OR). Takes precedence over <see cref="Type"/> when non-empty.</summary>
    public List<MessageType>? Types { get; set; }

    /// <summary>Free-text search across source/destination, GA name, DPT and decoded value.</summary>
    public string? Q { get; set; }

    /// <summary>Sort direction by timestamp. False = newest first (default).</summary>
    public bool Ascending { get; set; }
}
