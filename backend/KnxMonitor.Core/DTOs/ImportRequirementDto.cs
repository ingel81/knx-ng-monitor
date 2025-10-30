using KnxMonitor.Core.Enums;

namespace KnxMonitor.Core.DTOs;

public class ImportRequirementDto
{
    public RequirementType Type { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool IsFulfilled { get; set; }
    public int RemainingAttempts { get; set; } = 3; // For password attempts
}
