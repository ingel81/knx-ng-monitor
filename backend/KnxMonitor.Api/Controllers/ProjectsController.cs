using KnxMonitor.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnxMonitor.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ProjectsController : ControllerBase
{
    private readonly IProjectService _projectService;
    private readonly ILogger<ProjectsController> _logger;

    public ProjectsController(IProjectService projectService, ILogger<ProjectsController> logger)
    {
        _projectService = projectService;
        _logger = logger;
    }

    [HttpPost("upload")]
    public async Task<IActionResult> UploadProject(IFormFile file)
    {
        try
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded");

            if (!file.FileName.EndsWith(".knxproj", StringComparison.OrdinalIgnoreCase))
                return BadRequest("Invalid file type. Only .knxproj files are allowed");

            using var stream = file.OpenReadStream();
            var project = await _projectService.UploadProjectAsync(stream, file.FileName);

            _logger.LogInformation("Project {ProjectName} uploaded successfully with {GroupAddressCount} group addresses and {DeviceCount} devices",
                project.Name, project.GroupAddressCount, project.DeviceCount);

            return Ok(project);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Failed to upload project");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during project upload");
            return StatusCode(500, new { error = "An unexpected error occurred" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetAllProjects()
    {
        try
        {
            var projects = await _projectService.GetAllProjectsAsync();
            return Ok(projects);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve projects");
            return StatusCode(500, new { error = "Failed to retrieve projects" });
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetProjectDetails(int id)
    {
        try
        {
            var project = await _projectService.GetProjectDetailsAsync(id);

            if (project == null)
                return NotFound(new { error = "Project not found" });

            return Ok(project);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve project {ProjectId}", id);
            return StatusCode(500, new { error = "Failed to retrieve project details" });
        }
    }

    [HttpPut("{id}/activate")]
    public async Task<IActionResult> ActivateProject(int id)
    {
        try
        {
            var success = await _projectService.ActivateProjectAsync(id);

            if (!success)
                return NotFound(new { error = "Project not found" });

            _logger.LogInformation("Project {ProjectId} activated", id);
            return Ok(new { message = "Project activated successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to activate project {ProjectId}", id);
            return StatusCode(500, new { error = "Failed to activate project" });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteProject(int id)
    {
        try
        {
            var success = await _projectService.DeleteProjectAsync(id);

            if (!success)
                return NotFound(new { error = "Project not found" });

            _logger.LogInformation("Project {ProjectId} deleted", id);
            return Ok(new { message = "Project deleted successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete project {ProjectId}", id);
            return StatusCode(500, new { error = "Failed to delete project" });
        }
    }
}
