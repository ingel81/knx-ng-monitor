using KnxMonitor.Core.DTOs;
using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Interfaces;
using KnxMonitor.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace KnxMonitor.Infrastructure.Services;

public class ProjectService : IProjectService
{
    private readonly IProjectRepository _projectRepository;
    private readonly IGroupAddressRepository _groupAddressRepository;
    private readonly IKnxProjectParserService _parserService;
    private readonly IGroupAddressCacheService _cacheService;
    private readonly IKnxConnectionService _knxService;
    private readonly IKnxConfigurationRepository _configRepository;

    public ProjectService(
        IProjectRepository projectRepository,
        IGroupAddressRepository groupAddressRepository,
        IKnxProjectParserService parserService,
        IGroupAddressCacheService cacheService,
        IKnxConnectionService knxService,
        IKnxConfigurationRepository configRepository)
    {
        _projectRepository = projectRepository;
        _groupAddressRepository = groupAddressRepository;
        _parserService = parserService;
        _cacheService = cacheService;
        _knxService = knxService;
        _configRepository = configRepository;
    }

    public async Task<ProjectDto> UploadProjectAsync(Stream fileStream, string fileName)
    {
        // Create project entry
        var project = new Project
        {
            Name = Path.GetFileNameWithoutExtension(fileName),
            FileName = fileName,
            ImportDate = DateTime.UtcNow,
            IsActive = false
        };

        await _projectRepository.AddAsync(project);

        try
        {
            // Parse the .knxproj file
            var parsed = await _parserService.ParseProjectFileAsync(fileStream, project.Id);

            // Save parsed data
            project.GroupAddresses = parsed.GroupAddresses;
            project.Devices = parsed.Devices;
            project.Locations = parsed.Locations;
            project.CommunicationObjects = parsed.CommunicationObjects;
            project.GroupRanges = parsed.GroupRanges;
            project.KeyringKeys = parsed.KeyringKeys;

            await _projectRepository.UpdateAsync(project);

            return new ProjectDto
            {
                Id = project.Id,
                Name = project.Name,
                FileName = project.FileName,
                ImportDate = project.ImportDate,
                IsActive = project.IsActive,
                GroupAddressCount = parsed.GroupAddresses.Count,
                DeviceCount = parsed.Devices.Count
            };
        }
        catch (Exception ex)
        {
            // Delete project if parsing failed
            await _projectRepository.DeleteAsync(project);
            throw new InvalidOperationException($"Failed to import project: {ex.Message}", ex);
        }
    }

    public async Task<List<ProjectDto>> GetAllProjectsAsync()
    {
        var projects = await _projectRepository.GetAllWithCountsAsync();

        return projects.Select(p => new ProjectDto
        {
            Id = p.Id,
            Name = p.Name,
            FileName = p.FileName,
            ImportDate = p.ImportDate,
            IsActive = p.IsActive,
            GroupAddressCount = p.GroupAddresses.Count,
            DeviceCount = p.Devices.Count
        }).ToList();
    }

    public async Task<ProjectDetailsDto?> GetProjectDetailsAsync(int id)
    {
        var project = await _projectRepository.GetWithDetailsAsync(id);

        if (project == null)
            return null;

        return new ProjectDetailsDto
        {
            Id = project.Id,
            Name = project.Name,
            FileName = project.FileName,
            ImportDate = project.ImportDate,
            IsActive = project.IsActive,
            GroupAddresses = project.GroupAddresses.Select(ga => new GroupAddressDto
            {
                Id = ga.Id,
                Address = ga.Address,
                Name = ga.Name,
                Description = ga.Description,
                DatapointType = ga.DatapointType
            }).ToList(),
            Devices = project.Devices.Select(d => new DeviceDto
            {
                Id = d.Id,
                Name = d.Name,
                PhysicalAddress = d.PhysicalAddress,
                Manufacturer = d.Manufacturer,
                ProductName = d.ProductName
            }).ToList()
        };
    }

    public async Task<bool> ActivateProjectAsync(int id)
    {
        var project = await _projectRepository.GetByIdAsync(id);
        if (project == null)
            return false;

        // Atomically make exactly this project active (set-based, single transaction).
        await _projectRepository.SetActiveExclusiveAsync(id);

        // Refresh the cache with the new active project
        await _cacheService.RefreshAsync();

        // Couple the bus to the active project (model: active ⇒ connected). If auto-connect is
        // enabled, (re)connect now — this also clears any manual-disconnect latch left by a
        // previous deactivate, so re-activating restores the live link.
        var configs = await _configRepository.GetAllAsync();
        var config = configs.FirstOrDefault(c => c.IsActive) ?? configs.FirstOrDefault();
        if (config != null && config.AutoConnect && !_knxService.IsConnected)
        {
            try { await _knxService.ConnectAsync(config); }
            catch { /* non-fatal: the auto-connect worker will retry */ }
        }

        return true;
    }

    public async Task<bool> DeactivateProjectAsync(int id)
    {
        var project = await _projectRepository.GetByIdAsync(id);
        if (project == null)
            return false;

        if (!project.IsActive)
            return true; // already inactive — nothing to do

        project.IsActive = false;
        await _projectRepository.UpdateAsync(project);

        // No active project anymore: clear the GA cache and sever the bus link. DisconnectAsync
        // latches ManualDisconnect, so the auto-connect worker won't immediately reconnect.
        await _cacheService.RefreshAsync();
        await _knxService.DisconnectAsync();

        return true;
    }

    public async Task<bool> DeleteProjectAsync(int id)
    {
        var project = await _projectRepository.GetByIdAsync(id);
        if (project == null)
            return false;

        var wasActive = project.IsActive;

        // Set-based bulk delete (telegrams preserved, GA refs nulled in one indexed UPDATE).
        var deleted = await _projectRepository.DeleteProjectFastAsync(id);
        if (!deleted)
            return false;

        // If the deleted project was active, no project is active anymore: clear cache + drop bus.
        if (wasActive)
        {
            await _cacheService.RefreshAsync();
            await _knxService.DisconnectAsync();
        }

        return true;
    }

    public async Task<ProjectDeletePreviewDto?> GetDeletePreviewAsync(int id)
    {
        var project = await _projectRepository.GetWithDetailsAsync(id);
        if (project == null)
            return null;

        return new ProjectDeletePreviewDto
        {
            GroupAddressCount = project.GroupAddresses.Count,
            DeviceCount = project.Devices.Count,
            TelegramCount = await _projectRepository.CountTelegramsForProjectAsync(id)
        };
    }

    public async Task<List<LocationDto>?> GetLocationsAsync(int id)
    {
        if (!await _projectRepository.ExistsAsync(id))
            return null;

        var locations = await _projectRepository.GetLocationsAsync(id);

        return locations.Select(l => new LocationDto
        {
            Id = l.Id,
            ExternalId = l.ExternalId,
            Name = l.Name,
            Type = l.Type,
            ParentExternalId = l.ParentExternalId,
            DeviceAddresses = SplitList(l.DeviceAddresses),
            GroupAddresses = SplitList(l.GroupAddresses)
        }).ToList();
    }

    public async Task<List<CommunicationObjectDto>?> GetCommObjectsAsync(int id, string? groupAddress)
    {
        if (!await _projectRepository.ExistsAsync(id))
            return null;

        var comObjects = await _projectRepository.GetCommObjectsAsync(id, groupAddress);

        // One lookup dict (DeviceAddress -> device) for the whole call, so we can resolve the device
        // name/manufacturer/product behind each com object's physical address without an extra query per row.
        var devicesByAddress = (await _projectRepository.GetDevicesAsync(id))
            .Where(d => !string.IsNullOrEmpty(d.PhysicalAddress))
            .GroupBy(d => d.PhysicalAddress)
            .ToDictionary(g => g.Key, g => g.First());

        return comObjects.Select(c =>
        {
            devicesByAddress.TryGetValue(c.DeviceAddress, out var device);
            return new CommunicationObjectDto
            {
                Id = c.Id,
                DeviceAddress = c.DeviceAddress,
                DeviceName = device?.Name,
                DeviceManufacturer = device?.Manufacturer,
                DeviceProductName = device?.ProductName,
                Number = c.Number,
                Name = c.Name,
                FunctionText = c.FunctionText,
                GroupAddressLink = c.GroupAddressLink,
                DatapointType = c.DatapointType,
                Flags = c.Flags
            };
        }).ToList();
    }

    public async Task<List<GroupRangeDto>?> GetGroupRangesAsync(int id)
    {
        if (!await _projectRepository.ExistsAsync(id))
            return null;

        var ranges = await _projectRepository.GetGroupRangesAsync(id);

        return ranges.Select(r => new GroupRangeDto
        {
            Name = r.Name,
            RangeStart = r.RangeStart,
            RangeEnd = r.RangeEnd
        }).ToList();
    }

    public async Task<DeviceDto?> GetDeviceByAddressAsync(int projectId, string physicalAddress)
    {
        if (!await _projectRepository.ExistsAsync(projectId))
            return null;

        var device = await _projectRepository.GetDeviceByAddressAsync(projectId, physicalAddress);
        if (device == null)
            return null;

        return new DeviceDto
        {
            Id = device.Id,
            Name = device.Name,
            PhysicalAddress = device.PhysicalAddress,
            Manufacturer = device.Manufacturer,
            ProductName = device.ProductName
        };
    }

    public async Task<KeyringUploadResultDto?> UploadKeyringAsync(int id, byte[] keyringData, string keyringPassword)
    {
        if (!await _projectRepository.ExistsAsync(id))
            return null;

        // Decrypt + map (throws InvalidOperationException on bad password/format — surfaced by controller).
        var keys = await _parserService.ParseKeyringAsync(keyringData, keyringPassword, id);

        // Replace any previously stored keyring material for this project.
        await _projectRepository.ReplaceKeyringKeysAsync(id, keys);

        // Also persist the RAW keyring bytes + password so KNX Data Secure can Load() them at
        // connect time (the decrypted per-GA keys above are not sufficient for that Load() call).
        // SECURITY: stores the keyring password at rest — see ProjectKeyringBlob.
        await _projectRepository.UpsertKeyringBlobAsync(id, keyringData, keyringPassword);

        return new KeyringUploadResultDto
        {
            TotalKeys = keys.Count,
            GroupAddressKeys = keys.Count(k => k.KeyType == "GroupAddress"),
            ToolKeys = keys.Count(k => k.KeyType == "ToolKey"),
            HasBackboneKey = keys.Any(k => k.KeyType == "Backbone")
        };
    }

    private static List<string> SplitList(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? new List<string>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
}
