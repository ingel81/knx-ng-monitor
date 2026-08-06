using KnxMonitor.Core.DTOs;
using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnxMonitor.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class RecordingController : ControllerBase
{
    private readonly IRecordingSettingsProvider _provider;
    private readonly ILogger<RecordingController> _logger;

    public RecordingController(IRecordingSettingsProvider provider, ILogger<RecordingController> logger)
    {
        _provider = provider;
        _logger = logger;
    }

    /// <summary>Current recording settings: hot-buffer size and cold-archive options.</summary>
    /// <remarks>
    /// Served from the cached snapshot that the recording services themselves read, so this needs
    /// no database round-trip.
    /// </remarks>
    [HttpGet("settings")]
    public IActionResult GetSettings()
    {
        var current = _provider.Current;
        return Ok(new RecordingSettingsDto
        {
            HotBufferMaxCount = current.HotBufferMaxCount,
            ArchiveEnabled = current.ArchiveEnabled,
            ArchiveRetentionDays = current.ArchiveRetentionDays
        });
    }

    /// <summary>Updates the recording settings; they take effect on the running recording.</summary>
    /// <remarks>
    /// The values are persisted and swapped into the snapshot the recording workers read, so no
    /// restart is needed. What the fields do:
    /// HotBufferMaxCount is the number of telegrams kept in the database — a retention pass
    /// periodically deletes everything older than the newest HotBufferMaxCount rows, so lowering
    /// the value discards the oldest telegrams. Deletion happens chunked on the following passes,
    /// not the instant the value is saved.
    /// ArchiveEnabled toggles the additional NDJSON+gzip day-files; switching it off stops new
    /// writes but leaves existing archive files untouched.
    /// ArchiveRetentionDays deletes archive day-files older than that many days, null keeps them
    /// forever. The sweep runs at startup and at the daily file rollover, so a shortened retention
    /// only takes effect at the next sweep.
    /// Validation: HotBufferMaxCount must be greater than 0, ArchiveRetentionDays must be null or
    /// greater than 0 — otherwise 400 and nothing is changed.
    /// </remarks>
    /// <returns>The settings as they are now in effect.</returns>
    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings([FromBody] RecordingSettingsDto dto)
    {
        if (dto.HotBufferMaxCount <= 0)
        {
            return BadRequest(new { Message = "HotBufferMaxCount must be greater than 0" });
        }
        if (dto.ArchiveRetentionDays is <= 0)
        {
            return BadRequest(new { Message = "ArchiveRetentionDays must be greater than 0 or null" });
        }

        await _provider.UpdateAsync(new RecordingSettings
        {
            HotBufferMaxCount = dto.HotBufferMaxCount,
            ArchiveEnabled = dto.ArchiveEnabled,
            ArchiveRetentionDays = dto.ArchiveRetentionDays
        });

        var current = _provider.Current;
        return Ok(new RecordingSettingsDto
        {
            HotBufferMaxCount = current.HotBufferMaxCount,
            ArchiveEnabled = current.ArchiveEnabled,
            ArchiveRetentionDays = current.ArchiveRetentionDays
        });
    }
}
