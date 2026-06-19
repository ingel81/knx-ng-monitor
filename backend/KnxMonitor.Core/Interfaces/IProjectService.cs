using KnxMonitor.Core.DTOs;

namespace KnxMonitor.Core.Interfaces;

public interface IProjectService
{
    Task<ProjectDto> UploadProjectAsync(Stream fileStream, string fileName);
    Task<List<ProjectDto>> GetAllProjectsAsync();
    Task<ProjectDetailsDto?> GetProjectDetailsAsync(int id);
    Task<bool> ActivateProjectAsync(int id);

    /// <summary>Deactivates the active project: clears the GA cache and severs the bus link
    /// (latched, so the auto-connect worker stays away). No-op if already inactive.</summary>
    Task<bool> DeactivateProjectAsync(int id);

    Task<bool> DeleteProjectAsync(int id);

    /// <summary>Counts (GAs, devices, mapped telegrams) for the delete-confirmation dialog.</summary>
    Task<ProjectDeletePreviewDto?> GetDeletePreviewAsync(int id);

    /// <summary>Building/location hierarchy of a project as a flat list (frontend builds the tree).
    /// Returns null if the project does not exist.</summary>
    Task<List<LocationDto>?> GetLocationsAsync(int id);

    /// <summary>Communication objects of a project, optionally filtered to a single group address.
    /// Returns null if the project does not exist.</summary>
    Task<List<CommunicationObjectDto>?> GetCommObjectsAsync(int id, string? groupAddress);

    /// <summary>Main/middle group ranges of a project (section names for the GA tree), ordered by
    /// RangeStart. Returns null if the project does not exist.</summary>
    Task<List<GroupRangeDto>?> GetGroupRangesAsync(int id);

    /// <summary>Resolves a single device of a project by its exact physical address string
    /// (e.g. "1.0.59"). Returns null if the project or the device does not exist.</summary>
    Task<DeviceDto?> GetDeviceByAddressAsync(int projectId, string physicalAddress);

    /// <summary>Decrypts a keyring (.knxkeys) and stores its keys against the project, replacing any
    /// existing keyring keys. Returns null if the project does not exist.</summary>
    Task<KeyringUploadResultDto?> UploadKeyringAsync(int id, byte[] keyringData, string keyringPassword);
}
