using KnxMonitor.ProjectParser.Core.Enums;

namespace KnxMonitor.ProjectParser.Core.Models;

public class ParserProgress
{
    public ParseStep Step { get; set; }
    public int PercentComplete { get; set; }  // 0-100
    public string? Message { get; set; }
    public int? ItemsProcessed { get; set; }
    public int? TotalItems { get; set; }
}
