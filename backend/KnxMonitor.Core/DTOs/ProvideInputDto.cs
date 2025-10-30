using KnxMonitor.Core.Enums;

namespace KnxMonitor.Core.DTOs;

public class ProvideInputDto
{
    public RequirementType Type { get; set; }
    public string? Password { get; set; }
    public string? KeyringFile { get; set; } // Base64-encoded
    public string? KeyringPassword { get; set; }
}
