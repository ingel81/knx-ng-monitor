using KnxMonitor.Core.Entities;

namespace KnxMonitor.Core.Interfaces;

public interface IProjectRepository : IRepository<Project>
{
    Task<Project?> GetWithDetailsAsync(int id);
    Task<List<Project>> GetAllWithCountsAsync();
    Task<Project?> GetActiveProjectAsync();

    /// <summary>Number of recorded telegrams currently mapped to this project's group addresses.</summary>
    Task<int> CountTelegramsForProjectAsync(int id);

    /// <summary>
    /// Deletes a project and its group addresses + devices using set-based bulk operations.
    /// Telegrams are preserved: their GroupAddressId is nulled in a single indexed UPDATE
    /// instead of EF's row-by-row cascade. Returns false if the project does not exist.
    /// </summary>
    Task<bool> DeleteProjectFastAsync(int id);

    /// <summary>Atomically makes exactly one project active: set-based clear of all others +
    /// activate the target, in a single transaction (no per-row tracking).</summary>
    Task SetActiveExclusiveAsync(int id);

    /// <summary>Locations for a project, ordered by Id. Empty list if none.</summary>
    Task<List<Location>> GetLocationsAsync(int id);

    /// <summary>Communication objects for a project, optionally filtered to a single GA link.</summary>
    Task<List<CommunicationObject>> GetCommObjectsAsync(int id, string? groupAddress);

    /// <summary>Main/middle group ranges for a project, ordered by RangeStart. Empty list if none.</summary>
    Task<List<GroupRange>> GetGroupRangesAsync(int id);

    /// <summary>All devices of a project (PhysicalAddress, Name, Manufacturer, ProductName).
    /// Used to enrich communication objects and to resolve a single device by address.</summary>
    Task<List<Device>> GetDevicesAsync(int id);

    /// <summary>Resolves a single device of a project by its exact physical address string
    /// (e.g. "1.0.59"). Returns null if no device matches.</summary>
    Task<Device?> GetDeviceByAddressAsync(int projectId, string physicalAddress);

    /// <summary>Replaces all keyring keys for a project in one set-based delete + bulk insert.</summary>
    Task ReplaceKeyringKeysAsync(int id, IEnumerable<ProjectKeyringKey> keys);

    /// <summary>
    /// Stores (or replaces) the raw .knxkeys blob + password for a project, used by KNX Data
    /// Secure runtime decryption. Replace-semantics: at most one blob per project.
    /// SECURITY: the password is persisted at rest — see <see cref="ProjectKeyringBlob"/>.
    /// </summary>
    Task UpsertKeyringBlobAsync(int projectId, byte[] keyringFile, string keyringPassword);

    /// <summary>Returns the stored keyring blob for a project, or null if none exists.</summary>
    Task<ProjectKeyringBlob?> GetKeyringBlobAsync(int projectId);

    /// <summary>Finds an existing project by its stable ETS project id (P-XXXX), or null.</summary>
    Task<Project?> GetByEtsProjectIdAsync(string etsProjectId);

    /// <summary>
    /// Re-import into an EXISTING project, preserving telegram history. Group addresses are
    /// merged by Address (matched rows are updated in place so their Id — and thus every
    /// telegram pointing at it — survives; new addresses are inserted, vanished ones removed).
    /// Devices/Locations/CommObjects/GroupRanges are replaced wholesale (no telegram dependency).
    /// Updates project metadata (Name, FileName, ImportDate). Runs in one transaction.
    /// </summary>
    Task MergeReimportAsync(
        int projectId,
        string fileName,
        IEnumerable<GroupAddress> groupAddresses,
        IEnumerable<Device> devices,
        IEnumerable<Location> locations,
        IEnumerable<CommunicationObject> commObjects,
        IEnumerable<GroupRange> groupRanges);
}
