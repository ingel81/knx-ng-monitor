namespace KnxMonitor.Core.Entities;

public class GroupAddress
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string Address { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? DatapointType { get; set; }

    /// <summary>
    /// Names of the ETS functions this group address belongs to, newline-separated in project
    /// order; null when it is in none (always for ETS 4, which has no functions). A function name
    /// may contain commas but never a line break, hence the separator.
    /// </summary>
    public string? Functions { get; set; }

    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public Project Project { get; set; } = null!;
    public ICollection<KnxTelegram> Telegrams { get; set; } = new List<KnxTelegram>();
}
