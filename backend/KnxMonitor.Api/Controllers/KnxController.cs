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
            State = _knxService.State.ToString(),
            ManualDisconnect = _knxService.ManualDisconnect,
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

    /// <summary>
    /// Non-destructive connectivity probe — never interrupts the live recording.
    /// </summary>
    [HttpPost("test-connection")]
    public async Task<IActionResult> TestConnection([FromBody] int configurationId)
    {
        var config = await _configRepository.GetByIdAsync(configurationId);
        if (config == null)
        {
            return NotFound("Configuration not found");
        }

        var success = await _knxService.TestConnectionAsync(config);
        return success
            ? Ok(new { Success = true, Message = "Connection successful" })
            : Ok(new { Success = false, Message = "Connection failed" });
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
            AutoConnect = request.AutoConnect,
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

    [HttpPut("configurations/{id}")]
    public async Task<IActionResult> UpdateConfiguration(int id, [FromBody] CreateConfigRequest request)
    {
        var config = await _configRepository.GetByIdAsync(id);
        if (config == null)
        {
            return NotFound();
        }

        var connectionRelevantChange =
            config.IpAddress != request.IpAddress ||
            config.Port != request.Port ||
            config.ConnectionType != request.ConnectionType;

        config.IpAddress = request.IpAddress;
        config.Port = request.Port;
        config.PhysicalAddress = request.PhysicalAddress;
        config.ConnectionType = request.ConnectionType;
        config.AutoConnect = request.AutoConnect;
        config.UpdatedAt = DateTime.UtcNow;

        await _configRepository.UpdateAsync(config);

        // If the live link is up and the gateway endpoint actually changed, re-apply it
        // to the running connection so the new settings take effect without a restart.
        // ConnectAsync is non-latching (clears ManualDisconnect), so the worker stays happy.
        var reconnected = false;
        if (connectionRelevantChange && config.AutoConnect && _knxService.IsConnected)
        {
            try
            {
                reconnected = await _knxService.ConnectAsync(config);
            }
            catch (Exception ex)
            {
                // Non-fatal: the auto-connect worker will retry on its next tick.
                _logger.LogWarning(ex, "Reconnect after configuration change failed; worker will retry");
            }
        }

        return Ok(new { Configuration = config, Reconnected = reconnected });
    }
}

public record CreateConfigRequest(
    string IpAddress,
    int Port,
    string PhysicalAddress,
    ConnectionType ConnectionType,
    bool AutoConnect = true
);
