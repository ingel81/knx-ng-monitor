using KnxMonitor.Core.DTOs;
using KnxMonitor.Core.Interfaces;
using KnxMonitor.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnxMonitor.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ProjectsController : ControllerBase
{
    private readonly IProjectService _projectService;
    private readonly ProjectImportService _importService;
    private readonly ILogger<ProjectsController> _logger;

    public ProjectsController(
        IProjectService projectService,
        ProjectImportService importService,
        ILogger<ProjectsController> logger)
    {
        _projectService = projectService;
        _importService = importService;
        _logger = logger;
    }

    [HttpPost("upload")]
    public async Task<IActionResult> UploadProject(IFormFile file)
    {
        try
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { error = "No file uploaded" });

            if (!file.FileName.EndsWith(".knxproj", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { error = "Invalid file type. Only .knxproj files are allowed" });

            // Read file data
            using var memoryStream = new MemoryStream();
            await file.CopyToAsync(memoryStream);
            var fileData = memoryStream.ToArray();

            // Start import job
            var job = await _importService.StartImportAsync(file.FileName, fileData);

            _logger.LogInformation("Project import started: JobId={JobId}, FileName={FileName}", job.Id, file.FileName);

            return Ok(job);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start project import");
            return StatusCode(500, new { error = "Failed to start project import" });
        }
    }

    [HttpGet("imports/{id}")]
    public IActionResult GetImportStatus(Guid id)
    {
        try
        {
            var job = _importService.GetImportStatus(id);

            if (job == null)
                return NotFound(new { error = "Import job not found" });

            return Ok(job);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get import status for job {JobId}", id);
            return StatusCode(500, new { error = "Failed to get import status" });
        }
    }

    [HttpPost("imports/{id}/provide-input")]
    public async Task<IActionResult> ProvideInput(Guid id, [FromBody] ProvideInputDto input)
    {
        try
        {
            var success = await _importService.ProvideInputAsync(id, input);

            if (!success)
                return BadRequest(new { error = "Failed to provide input. Job may not be in correct state." });

            return Ok(new { message = "Input provided successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to provide input for job {JobId}", id);
            return StatusCode(500, new { error = "Failed to provide input" });
        }
    }

    [HttpDelete("imports/{id}")]
    public IActionResult CancelImport(Guid id)
    {
        try
        {
            _importService.CancelImport(id);
            _logger.LogInformation("Import job {JobId} cancelled", id);
            return Ok(new { message = "Import cancelled successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel import job {JobId}", id);
            return StatusCode(500, new { error = "Failed to cancel import" });
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

    [HttpPut("{id}/deactivate")]
    public async Task<IActionResult> DeactivateProject(int id)
    {
        try
        {
            var success = await _projectService.DeactivateProjectAsync(id);

            if (!success)
                return NotFound(new { error = "Project not found" });

            _logger.LogInformation("Project {ProjectId} deactivated", id);
            return Ok(new { message = "Project deactivated successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deactivate project {ProjectId}", id);
            return StatusCode(500, new { error = "Failed to deactivate project" });
        }
    }

    [HttpGet("{id}/delete-preview")]
    public async Task<IActionResult> GetDeletePreview(int id)
    {
        try
        {
            var preview = await _projectService.GetDeletePreviewAsync(id);

            if (preview == null)
                return NotFound(new { error = "Project not found" });

            return Ok(preview);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build delete preview for project {ProjectId}", id);
            return StatusCode(500, new { error = "Failed to build delete preview" });
        }
    }

    [HttpGet("{id}/locations")]
    public async Task<IActionResult> GetLocations(int id)
    {
        try
        {
            var locations = await _projectService.GetLocationsAsync(id);

            if (locations == null)
                return NotFound(new { error = "Project not found" });

            return Ok(locations);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve locations for project {ProjectId}", id);
            return StatusCode(500, new { error = "Failed to retrieve locations" });
        }
    }

    [HttpGet("{id}/commobjects")]
    public async Task<IActionResult> GetCommObjects(int id, [FromQuery] string? address)
    {
        try
        {
            var comObjects = await _projectService.GetCommObjectsAsync(id, address);

            if (comObjects == null)
                return NotFound(new { error = "Project not found" });

            return Ok(comObjects);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve communication objects for project {ProjectId}", id);
            return StatusCode(500, new { error = "Failed to retrieve communication objects" });
        }
    }

    [HttpGet("{id}/groupranges")]
    public async Task<IActionResult> GetGroupRanges(int id)
    {
        try
        {
            var ranges = await _projectService.GetGroupRangesAsync(id);

            if (ranges == null)
                return NotFound(new { error = "Project not found" });

            return Ok(ranges);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve group ranges for project {ProjectId}", id);
            return StatusCode(500, new { error = "Failed to retrieve group ranges" });
        }
    }

    [HttpGet("{id}/device")]
    public async Task<IActionResult> GetDeviceByAddress(int id, [FromQuery] string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return BadRequest(new { error = "Query parameter 'address' is required" });

        try
        {
            var device = await _projectService.GetDeviceByAddressAsync(id, address);

            if (device == null)
                return NotFound(new { error = "Device not found" });

            return Ok(device);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve device {Address} for project {ProjectId}", address, id);
            return StatusCode(500, new { error = "Failed to resolve device" });
        }
    }

    [HttpPost("{id}/keyring")]
    public async Task<IActionResult> UploadKeyring(int id, IFormFile file, [FromForm] string? password)
    {
        try
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { error = "No keyring file uploaded" });

            if (!file.FileName.EndsWith(".knxkeys", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { error = "Invalid file type. Only .knxkeys files are allowed" });

            if (string.IsNullOrEmpty(password))
                return BadRequest(new { error = "Keyring password is required" });

            using var memoryStream = new MemoryStream();
            await file.CopyToAsync(memoryStream);

            var result = await _projectService.UploadKeyringAsync(id, memoryStream.ToArray(), password);

            if (result == null)
                return NotFound(new { error = "Project not found" });

            _logger.LogInformation("Keyring uploaded for project {ProjectId}: {Total} keys", id, result.TotalKeys);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            // Bad password or malformed keyring — user-actionable, not a server fault.
            _logger.LogWarning(ex, "Keyring upload rejected for project {ProjectId}", id);
            return BadRequest(new { error = "Failed to read keyring file. Check the file and password." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload keyring for project {ProjectId}", id);
            return StatusCode(500, new { error = "Failed to upload keyring" });
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
