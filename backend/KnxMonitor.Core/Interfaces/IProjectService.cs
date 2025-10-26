using KnxMonitor.Core.DTOs;

namespace KnxMonitor.Core.Interfaces;

public interface IProjectService
{
    Task<ProjectDto> UploadProjectAsync(Stream fileStream, string fileName);
    Task<List<ProjectDto>> GetAllProjectsAsync();
    Task<ProjectDetailsDto?> GetProjectDetailsAsync(int id);
    Task<bool> ActivateProjectAsync(int id);
    Task<bool> DeleteProjectAsync(int id);
}
