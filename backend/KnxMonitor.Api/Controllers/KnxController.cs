using KnxMonitor.Core.Interfaces;
using KnxMonitor.Core.Enums;
using KnxMonitor.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnxMonitor.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class KnxController : ControllerBase
{
    private readonly IKnxConnectionService _knxService;
    private readonly IKnxConfigurationRepository _configRepository;
    private readonly ILogger<KnxController> _logger;

    public KnxController(
        IKnxConnectionService knxService,
        IKnxConfigurationRepository configRepository,
        ILogger<KnxController> logger)
    {
        _knxService = knxService;
        _configRepository = configRepository;
        _logger = logger;
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var config = await _knxService.GetActiveConfigurationAsync();
        return Ok(new
        {
            IsConnected = _knxService.IsConnected,
            Configuration = config
        });
    }

    [HttpPost("connect")]
    public async Task<IActionResult> Connect([FromBody] int configurationId)
    {
        var config = await _configRepository.GetByIdAsync(configurationId);
        if (config == null)
        {
            return NotFound("Configuration not found");
        }

        var success = await _knxService.ConnectAsync(config);
        if (success)
        {
            return Ok(new { Message = "Connected successfully" });
        }

        return BadRequest(new { Message = "Failed to connect" });
    }

    [HttpPost("disconnect")]
    public async Task<IActionResult> Disconnect()
    {
        await _knxService.DisconnectAsync();
        return Ok(new { Message = "Disconnected successfully" });
    }

    [HttpPost("configurations")]
    public async Task<IActionResult> CreateConfiguration([FromBody] CreateConfigRequest request)
    {
        var config = new Core.Entities.KnxConfiguration
        {
            IpAddress = request.IpAddress,
            Port = request.Port,
            PhysicalAddress = request.PhysicalAddress,
            ConnectionType = request.ConnectionType,
            IsActive = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _configRepository.AddAsync(config);
        return CreatedAtAction(nameof(GetConfiguration), new { id = config.Id }, config);
    }

    [HttpGet("configurations/{id}")]
    public async Task<IActionResult> GetConfiguration(int id)
    {
        var config = await _configRepository.GetByIdAsync(id);
        if (config == null)
        {
            return NotFound();
        }

        return Ok(config);
    }

    [HttpGet("configurations")]
    public async Task<IActionResult> GetAllConfigurations()
    {
        var configs = await _configRepository.GetAllAsync();
        return Ok(configs);
    }
}

public record CreateConfigRequest(
    string IpAddress,
    int Port,
    string PhysicalAddress,
    ConnectionType ConnectionType
);
