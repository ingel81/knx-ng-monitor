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
}
