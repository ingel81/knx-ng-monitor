using KnxMonitor.Core.Enums;

namespace KnxMonitor.Core.Entities;

public class KnxConfiguration
{
    public int Id { get; set; }
    public string IpAddress { get; set; } = "192.168.1.100";
    public int Port { get; set; } = 3671;
    public ConnectionType ConnectionType { get; set; } = ConnectionType.Tunneling;
    public string PhysicalAddress { get; set; } = "1.0.58";
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
