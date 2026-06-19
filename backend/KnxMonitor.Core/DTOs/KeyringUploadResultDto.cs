namespace KnxMonitor.Core.DTOs;

/// <summary>Summary returned after a keyring (.knxkeys) upload-after-import.</summary>
public class KeyringUploadResultDto
{
    public int TotalKeys { get; set; }
    public int GroupAddressKeys { get; set; }
    public int ToolKeys { get; set; }
    public bool HasBackboneKey { get; set; }
}
