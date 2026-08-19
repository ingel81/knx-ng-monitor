using System.Collections.Concurrent;
using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KnxMonitor.Infrastructure.Services;

/// <summary>
/// Hot-path lookups into the active project: group address by destination, device by source.
/// </summary>
/// <remarks>
/// Both live on the same cache because they share a lifetime — they are loaded from one project and
/// become stale at the same moment (project switch, re-import, delete). A separate device cache
/// would need refreshing at every call site that refreshes this one, and would silently drift the
/// day someone forgets.
/// </remarks>
public interface IProjectCacheService
{
    Task InitializeAsync();
    Task RefreshAsync();
    GroupAddress? GetByAddress(string address);
    Task<GroupAddress?> GetByAddressAsync(string address);

    /// <summary>Device of the active project by individual address ("1.1.5"), or null.</summary>
    Device? GetDeviceByAddress(string physicalAddress);
}

public class ProjectCacheService : IProjectCacheService
{
    private readonly ILogger<ProjectCacheService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConcurrentDictionary<string, GroupAddress> _addressCache = new();
    private readonly ConcurrentDictionary<string, Device> _deviceCache = new();
    private int? _currentActiveProjectId;

    public ProjectCacheService(
        ILogger<ProjectCacheService> logger,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    public async Task InitializeAsync()
    {
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var projectRepository = scope.ServiceProvider.GetRequiredService<IProjectRepository>();
            var groupAddressRepository = scope.ServiceProvider.GetRequiredService<IGroupAddressRepository>();

            // Find the active project
            var activeProject = await projectRepository.GetActiveProjectAsync();

            if (activeProject == null)
            {
                _logger.LogInformation("No active project found, clearing cache");
                _addressCache.Clear();
                _deviceCache.Clear();
                _currentActiveProjectId = null;
                return;
            }

            // Check if we need to refresh (different project)
            if (_currentActiveProjectId == activeProject.Id && _addressCache.Any())
            {
                _logger.LogDebug("Cache already up-to-date for project {ProjectId}", activeProject.Id);
                return;
            }

            _logger.LogInformation("Loading group addresses for active project {ProjectName} (ID: {ProjectId})",
                activeProject.Name, activeProject.Id);

            // Load all group addresses for the active project
            var groupAddresses = await groupAddressRepository.GetByProjectIdAsync(activeProject.Id);

            _addressCache.Clear();
            foreach (var ga in groupAddresses)
            {
                _addressCache[ga.Address] = ga;
            }

            var devices = await projectRepository.GetDevicesAsync(activeProject.Id);

            _deviceCache.Clear();
            foreach (var device in devices)
            {
                if (!string.IsNullOrWhiteSpace(device.PhysicalAddress))
                {
                    _deviceCache[device.PhysicalAddress] = device;
                }
            }

            _currentActiveProjectId = activeProject.Id;

            _logger.LogInformation("Cached {Count} group addresses and {DeviceCount} devices for project {ProjectName}",
                _addressCache.Count, _deviceCache.Count, activeProject.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh project cache");
        }
    }

    public GroupAddress? GetByAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return null;

        return _addressCache.TryGetValue(address, out var groupAddress) ? groupAddress : null;
    }

    public Task<GroupAddress?> GetByAddressAsync(string address)
    {
        return Task.FromResult(GetByAddress(address));
    }

    public Device? GetDeviceByAddress(string physicalAddress)
    {
        if (string.IsNullOrWhiteSpace(physicalAddress))
            return null;

        return _deviceCache.TryGetValue(physicalAddress, out var device) ? device : null;
    }
}
