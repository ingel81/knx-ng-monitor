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
    public string DestinationAddress { get; set; } = string.Empty;
    public string? GroupAddressName { get; set; }
    public string? DatapointType { get; set; }
    public MessageType MessageType { get; set; }
    public string Value { get; set; } = string.Empty;
    public string? ValueDecoded { get; set; }
    public int Priority { get; set; }
    public string? Flags { get; set; }
}
