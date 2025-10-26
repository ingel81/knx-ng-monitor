using KnxMonitor.Core.DTOs;
using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace KnxMonitor.Infrastructure.Services;

public class ProjectService : IProjectService
{
    private readonly IProjectRepository _projectRepository;
    private readonly IGroupAddressRepository _groupAddressRepository;
    private readonly IKnxProjectParserService _parserService;
    private readonly IGroupAddressCacheService _cacheService;

    public ProjectService(
        IProjectRepository projectRepository,
        IGroupAddressRepository groupAddressRepository,
        IKnxProjectParserService parserService,
        IGroupAddressCacheService cacheService)
    {
        _projectRepository = projectRepository;
        _groupAddressRepository = groupAddressRepository;
        _parserService = parserService;
        _cacheService = cacheService;
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
            var (groupAddresses, devices) = await _parserService.ParseProjectFileAsync(fileStream, project.Id);

            // Save parsed data
            project.GroupAddresses = groupAddresses;
            project.Devices = devices;

            await _projectRepository.UpdateAsync(project);

            return new ProjectDto
            {
                Id = project.Id,
                Name = project.Name,
                FileName = project.FileName,
                ImportDate = project.ImportDate,
                IsActive = project.IsActive,
                GroupAddressCount = groupAddresses.Count,
                DeviceCount = devices.Count
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

        // Deactivate all other projects
        var allProjects = await _projectRepository.GetAllAsync();
        foreach (var p in allProjects)
        {
            p.IsActive = false;
            await _projectRepository.UpdateAsync(p);
        }

        // Activate the selected project
        project.IsActive = true;
        await _projectRepository.UpdateAsync(project);

        // Refresh the cache with the new active project
        await _cacheService.RefreshAsync();

        return true;
    }

    public async Task<bool> DeleteProjectAsync(int id)
    {
        var project = await _projectRepository.GetByIdAsync(id);
        if (project == null)
            return false;

        var wasActive = project.IsActive;
        await _projectRepository.DeleteAsync(project);

        // Refresh cache if the deleted project was active
        if (wasActive)
        {
            await _cacheService.RefreshAsync();
        }

        return true;
    }
}
