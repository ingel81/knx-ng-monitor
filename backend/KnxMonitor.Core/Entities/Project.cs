namespace KnxMonitor.Core.Entities;

public class Project
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTime ImportDate { get; set; }
    public bool IsActive { get; set; }
    public string? ProjectData { get; set; }

    /// <summary>
    /// Stable ETS project identifier (the inner "P-XXXX" id), constant across edits of the
    /// same project. Used to match a re-import to the existing project so historical telegrams
    /// (linked via group-address) keep their association instead of orphaning under a new id.
    /// </summary>
    public string? EtsProjectId { get; set; }

    // Navigation properties
    public ICollection<GroupAddress> GroupAddresses { get; set; } = new List<GroupAddress>();
    public ICollection<Device> Devices { get; set; } = new List<Device>();
    public ICollection<Location> Locations { get; set; } = new List<Location>();
    public ICollection<CommunicationObject> CommunicationObjects { get; set; } = new List<CommunicationObject>();
    public ICollection<GroupRange> GroupRanges { get; set; } = new List<GroupRange>();
    public ICollection<ProjectKeyringKey> KeyringKeys { get; set; } = new List<ProjectKeyringKey>();

    /// <summary>Raw .knxkeys blob + password for KNX Data Secure runtime decryption (one per project, optional).</summary>
    public ProjectKeyringBlob? KeyringBlob { get; set; }
}
