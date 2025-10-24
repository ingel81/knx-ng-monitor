namespace KnxMonitor.Core.Entities;

public class GroupAddress
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string Address { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? DatapointType { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public Project Project { get; set; } = null!;
    public ICollection<KnxTelegram> Telegrams { get; set; } = new List<KnxTelegram>();
}
