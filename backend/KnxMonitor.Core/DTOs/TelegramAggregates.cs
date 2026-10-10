namespace KnxMonitor.Core.DTOs;

/// <summary>Telegram count of one address (destination group address or source device) in a range.</summary>
public class AddressCount
{
    public string Address { get; set; } = string.Empty;
    public long Count { get; set; }
}
