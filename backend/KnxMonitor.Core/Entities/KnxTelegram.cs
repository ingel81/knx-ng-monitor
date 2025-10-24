using KnxMonitor.Core.Enums;

namespace KnxMonitor.Core.Entities;

public class KnxTelegram
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string SourceAddress { get; set; } = string.Empty;
    public string DestinationAddress { get; set; } = string.Empty;
    public int? GroupAddressId { get; set; }
    public MessageType MessageType { get; set; }
    public byte[] Value { get; set; } = Array.Empty<byte>();
    public string? ValueDecoded { get; set; }
    public int Priority { get; set; }
    public string? Flags { get; set; }

    // Navigation properties
    public GroupAddress? GroupAddress { get; set; }
}
