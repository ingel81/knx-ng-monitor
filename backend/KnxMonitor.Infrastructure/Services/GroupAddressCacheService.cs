using System.Collections.Concurrent;
using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KnxMonitor.Infrastructure.Services;

public interface IGroupAddressCacheService
{
    Task InitializeAsync();
    Task RefreshAsync();
    GroupAddress? GetByAddress(string address);
    Task<GroupAddress?> GetByAddressAsync(string address);
}

public class GroupAddressCacheService : IGroupAddressCacheService
{
    private readonly ILogger<GroupAddressCacheService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConcurrentDictionary<string, GroupAddress> _addressCache = new();
    private int? _currentActiveProjectId;

    public GroupAddressCacheService(
        ILogger<GroupAddressCacheService> logger,
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

            _currentActiveProjectId = activeProject.Id;

            _logger.LogInformation("Cached {Count} group addresses for project {ProjectName}",
                _addressCache.Count, activeProject.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh group address cache");
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
}
