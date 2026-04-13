namespace KnxMonitor.ProjectParser.Core.Models;

public class ParserOptions
{
    public string? Password { get; set; }
    public Stream? KeyringStream { get; set; }
    public string? KeyringPassword { get; set; }
    public bool ValidateStructure { get; set; } = true;
    public bool IncludeEmptyDevices { get; set; } = false;
    public bool ThrowOnWarnings { get; set; } = false;
}
