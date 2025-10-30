namespace KnxMonitor.Core.Models;

public class ImportContext
{
    public Guid JobId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public byte[] FileData { get; set; } = Array.Empty<byte>();
    public string? ProjectPassword { get; set; }
    public byte[]? KeyringFile { get; set; }
    public string? KeyringPassword { get; set; }
    public Action<string, int>? ProgressCallback { get; set; } // stepName, progress
    public ProjectFeatures? DetectedFeatures { get; set; }
}
