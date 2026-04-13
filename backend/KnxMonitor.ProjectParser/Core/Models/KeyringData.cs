namespace KnxMonitor.ProjectParser.Core.Models;

public class KeyringData
{
    public string? ProjectName { get; set; }
    public string? CreatedBy { get; set; }
    public string? Created { get; set; }
    public string? Signature { get; set; }
    public byte[]? BackboneKey { get; set; }
    public string? BackboneMulticastAddress { get; set; }
    public List<KeyringDevice> Devices { get; set; } = new();
    public List<KeyringGroupAddress> GroupAddresses { get; set; } = new();
}

public class KeyringDevice
{
    public string IndividualAddress { get; set; } = string.Empty;
    public byte[]? ToolKey { get; set; }
    public string? ManagementPassword { get; set; }
    public string? Authentication { get; set; }
}

public class KeyringGroupAddress
{
    public string Address { get; set; } = string.Empty;
    public byte[]? Key { get; set; }
}
