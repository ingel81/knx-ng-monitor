using KnxMonitor.Core.Enums;

namespace KnxMonitor.Core.Models;

public class ProjectFeatures
{
    public EtsVersion EtsVersion { get; set; }
    public bool IsPasswordProtected { get; set; }
    public bool HasKnxSecureDevices { get; set; }
    public bool RequiresKeyring { get; set; }
    public string? InnerZipFileName { get; set; } // P-xxxx.zip
}
