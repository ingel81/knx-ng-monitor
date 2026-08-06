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

    /// <summary>Uploads an ETS project file and starts the import as a background job.</summary>
    /// <remarks>
    /// Returns as soon as the job exists — the parse itself runs in the background, so poll
    /// <c>GET imports/{id}</c> for progress. Only files ending in <c>.knxproj</c> are accepted,
    /// and the request body is capped at 200 MB.
    ///
    /// If the archive is password protected the job stops in <c>WaitingForInput</c> and lists what
    /// it needs; feed those inputs to <c>POST imports/{id}/provide-input</c>, then the job continues
    /// on its own. Re-uploading a project that was imported before is recognised by its ETS project
    /// id and updates the existing project in place, so recorded telegrams keep their group-address
    /// link. A genuinely new project is auto-activated when no other project is active yet.
    /// </remarks>
    /// <param name="file">The <c>.knxproj</c> file, sent as multipart/form-data.</param>
    /// <returns>The created import job: id, status, the step list and (once finished) the project id and counts.</returns>
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

    /// <summary>Returns the current state of an import job.</summary>
    /// <remarks>
    /// Jobs are held in memory only. A job that has completed, failed or been cancelled is swept
    /// an hour after it terminated (the cleanup worker runs every five minutes) and is reported as
    /// not found from then on.
    /// </remarks>
    /// <param name="id">Job id returned by the upload call.</param>
    /// <returns>Status, overall progress, per-step detail and the outstanding requirements.</returns>
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

    /// <summary>Supplies one requested input — project password, keyring file or keyring password — to a waiting import job.</summary>
    /// <remarks>
    /// Each call fulfils exactly the requirement named in <c>type</c>; the keyring file is passed
    /// Base64-encoded. The call is only accepted while the job is in <c>WaitingForInput</c> and
    /// rejected otherwise.
    ///
    /// Supplying the project password triggers a second look inside the now decryptable archive:
    /// if KNX Secure devices turn up, an optional keyring file and keyring password requirement are
    /// added and the job stays in <c>WaitingForInput</c>. As soon as every listed requirement is
    /// marked fulfilled, the import resumes in the background — poll <c>GET imports/{id}</c> from
    /// there. The password is not verified at this point; a wrong one surfaces as a failed job.
    /// </remarks>
    /// <param name="id">Job id of the waiting import.</param>
    /// <param name="input">The values for the requirements the job is waiting on.</param>
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

    /// <summary>Marks an import job as cancelled.</summary>
    /// <remarks>
    /// This flips the job state only. A parse already running in the background is not interrupted
    /// and a project it has written stays. Unknown job ids are accepted without effect.
    /// </remarks>
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

    /// <summary>Lists all imported projects.</summary>
    /// <returns>Per project: name, source file name, import date, active flag and the group-address and device counts.</returns>
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

    /// <summary>Returns one project together with its group addresses and devices.</summary>
    /// <remarks>
    /// Both lists come back complete and unpaged, so the response grows with the size of the project.
    /// </remarks>
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

    /// <summary>Makes this project the active one.</summary>
    /// <remarks>
    /// Activation is exclusive: any other active project is deactivated in the same transaction,
    /// so at most one project is ever active. Afterwards the group-address cache is refreshed and
    /// incoming telegrams resolve to this project's names and DPTs. The bus connection is
    /// deliberately left untouched — activating decodes, it does not connect.
    /// </remarks>
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

    /// <summary>Deactivates the project, leaving no project active.</summary>
    /// <remarks>
    /// The group-address cache is cleared, so telegrams are shown undecoded — raw hex, without name
    /// or DPT. Monitoring keeps running: the bus link stays up. Deactivating a project that is
    /// already inactive succeeds and changes nothing.
    /// </remarks>
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

    /// <summary>Reports what deleting this project would affect, for the confirmation dialog.</summary>
    /// <returns>
    /// The project's group-address and device counts plus the number of recorded telegrams mapped
    /// to it. Those telegrams survive the deletion — they only lose their group-address mapping.
    /// </returns>
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

    /// <summary>Returns the project's locations (buildings, floors, rooms, …).</summary>
    /// <remarks>
    /// A flat list, not a tree — the hierarchy is expressed through each entry's parent id. Every
    /// entry also carries the device and group addresses assigned to that location.
    /// </remarks>
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

    /// <summary>Returns the project's communication objects, optionally only those linked to one group address.</summary>
    /// <remarks>
    /// Each entry is enriched with the name, manufacturer and product name of the device behind its
    /// physical address, as far as that device is part of the project. Sorted by device address,
    /// then by object number.
    /// </remarks>
    /// <param name="id">Project id.</param>
    /// <param name="address">Group address (<c>m/m/s</c>). When set, only objects whose group-address link matches it exactly are returned.</param>
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

    /// <summary>Returns the project's group ranges — the named main and middle groups — ordered by range start.</summary>
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

    /// <summary>Resolves a single device of the project by its physical address.</summary>
    /// <remarks>
    /// The address is matched exactly, and an unknown project id is reported the same way as an
    /// unknown device.
    /// </remarks>
    /// <param name="id">Project id.</param>
    /// <param name="address">Physical address (<c>a.l.d</c>) to look up. Required.</param>
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

    /// <summary>Attaches a keyring (<c>.knxkeys</c>) to an already imported project.</summary>
    /// <remarks>
    /// Multipart upload of the file plus its password; both are required and only files ending in
    /// <c>.knxkeys</c> are accepted. Any keyring material previously stored for the project is
    /// replaced — both the decrypted per-key rows and the raw keyring bytes, which KNX Data Secure
    /// needs when it loads the keyring at connect time. Because of that the keyring password is
    /// stored with the project. A wrong password or an unreadable file is reported as a client error.
    /// </remarks>
    /// <param name="id">Project the keyring belongs to.</param>
    /// <param name="file">The <c>.knxkeys</c> keyring file.</param>
    /// <param name="password">Password of the keyring file. Required.</param>
    /// <returns>How many keys were read, split into group-address and tool keys, and whether a backbone key is present.</returns>
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

    /// <summary>Deletes a project and everything parsed from it.</summary>
    /// <remarks>
    /// Removes the project's group addresses, devices, locations, communication objects, group
    /// ranges and stored keyring material in one transaction. Recorded telegrams are kept but lose
    /// their group-address link and are shown undecoded from then on. Deleting the active project
    /// leaves no project active; the bus link stays up.
    /// </remarks>
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
