using KnxMonitor.Core.Enums;

namespace KnxMonitor.Core.DTOs;

/// <summary>
/// History row shape. Mirrors the SignalR "NewTelegram" payload so the frontend can reuse
/// the same model. <see cref="Value"/> is the raw payload as an upper-case hex string.
/// </summary>
public class TelegramDto
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string SourceAddress { get; set; } = string.Empty;

    /// <summary>
    /// Device name behind <see cref="SourceAddress"/>, resolved from the active project. Null when
    /// no project is active or the address is not in it. Unlike <see cref="GroupAddressName"/>,
    /// which follows the telegram's stored link, this is looked up per request: history rows
    /// recorded under an older project therefore show today's device names.
    /// </summary>
    public string? SourceName { get; set; }

    /// <summary>
    /// Building part of the sender, compact: the space the device sits in plus every higher level
    /// that has siblings, joined by " › " (e.g. "00/2_WR › 00/3_EG › 01_EGD"). Resolved from the
    /// active project like <see cref="SourceName"/>; null when the device is in no location.
    /// </summary>
    public string? SourceLocation { get; set; }

    /// <summary>Full building path of the sender, root first, same separator. Null like <see cref="SourceLocation"/>.</summary>
    public string? SourceLocationPath { get; set; }

    public string DestinationAddress { get; set; } = string.Empty;

    /// <summary>
    /// ETS functions the destination group address belongs to in the active project (ETS 5/6
    /// only). Empty when it is in none. Resolved per request like <see cref="SourceName"/>.
    /// </summary>
    public IReadOnlyList<string> Functions { get; set; } = Array.Empty<string>();

    public string? GroupAddressName { get; set; }
    public string? DatapointType { get; set; }
    public MessageType MessageType { get; set; }
    public string Value { get; set; } = string.Empty;
    public string? ValueDecoded { get; set; }
    public int Priority { get; set; }
    public string? Flags { get; set; }
}
