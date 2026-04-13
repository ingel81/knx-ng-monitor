using KnxMonitor.ProjectParser.Core.Enums;

namespace KnxMonitor.ProjectParser.Core.Models;

public class ProjectFeatures
{
    public EtsVersion EtsVersion { get; set; }
    public bool HasPassword { get; set; }
    public bool HasKnxSecure { get; set; }
    public string? ProjectId { get; set; }
    public AddressingStyle AddressingStyle { get; set; } = AddressingStyle.ThreeLevel;
}
