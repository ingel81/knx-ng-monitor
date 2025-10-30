using KnxMonitor.Core.Enums;

namespace KnxMonitor.Core.DTOs;

public class ImportJobDto
{
    public Guid Id { get; set; }
    public ImportStatus Status { get; set; }
    public int OverallProgress { get; set; } // 0-100
    public List<ImportStepDto> Steps { get; set; } = new();
    public List<ImportRequirementDto> Requirements { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }

    // Result data (when completed)
    public int? ProjectId { get; set; }
    public string? ProjectName { get; set; }
    public int? GroupAddressCount { get; set; }
    public int? DeviceCount { get; set; }
    public EtsVersion? EtsVersion { get; set; }
    public bool HasKnxSecure { get; set; }
}
