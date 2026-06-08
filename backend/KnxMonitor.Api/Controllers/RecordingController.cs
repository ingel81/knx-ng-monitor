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
