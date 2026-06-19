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

    /// <summary>
    /// Auto-connect state for the effective configuration (active one, else the first).
    /// The active project is coupled to the bus link, so this governs whether it auto-connects.
    /// </summary>
    [HttpGet("autoconnect")]
    public async Task<IActionResult> GetAutoConnect()
    {
        var config = await GetEffectiveConfigAsync();
        return Ok(new { Enabled = config?.AutoConnect ?? true, Connected = _knxService.IsConnected });
    }

    [HttpPut("autoconnect")]
    public async Task<IActionResult> SetAutoConnect([FromBody] SetAutoConnectRequest request)
    {
        var config = await GetEffectiveConfigAsync();
        if (config == null)
            return NotFound(new { error = "No KNX configuration found" });

        config.AutoConnect = request.Enabled;
        config.UpdatedAt = DateTime.UtcNow;
        await _configRepository.UpdateAsync(config);

        // Apply immediately: enabling connects now (clears the manual-disconnect latch),
        // disabling severs the link and latches so the worker stays away.
        if (request.Enabled)
        {
            if (!_knxService.IsConnected)
            {
                try { await _knxService.ConnectAsync(config); }
                catch (Exception ex) { _logger.LogWarning(ex, "Auto-connect enable: immediate connect failed; worker will retry"); }
            }
        }
        else if (_knxService.IsConnected)
        {
            await _knxService.DisconnectAsync();
        }

        return Ok(new { Enabled = config.AutoConnect, Connected = _knxService.IsConnected });
    }

    /// <summary>
    /// Sends a GroupValueWrite to the bus. ⚠ Writes to the real KNX installation — the frontend
    /// gates this behind an explicit confirmation dialog.
    /// </summary>
    [HttpPost("write")]
    public async Task<IActionResult> WriteGroupValue([FromBody] WriteGroupValueRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Address))
            return BadRequest(new { Success = false, Message = "Address is required" });

        var ok = await _knxService.WriteGroupValueAsync(request.Address, request.DatapointType, request.Value ?? string.Empty);
        return ok
            ? Ok(new { Success = true, Message = "Value written" })
            : BadRequest(new { Success = false, Message = "Write failed — not connected or value could not be encoded" });
    }

    /// <summary>Sends a GroupValueRead; the response shows up as a normal telegram.</summary>
    [HttpPost("read")]
    public async Task<IActionResult> ReadGroupValue([FromBody] ReadGroupValueRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Address))
            return BadRequest(new { Success = false, Message = "Address is required" });

        var ok = await _knxService.ReadGroupValueAsync(request.Address);
        return ok
            ? Ok(new { Success = true, Message = "Read request sent" })
            : BadRequest(new { Success = false, Message = "Read request failed — not connected" });
    }

    /// <summary>Effective configuration: the active one if any, else the first stored config.</summary>
    private async Task<Core.Entities.KnxConfiguration?> GetEffectiveConfigAsync()
    {
        var configs = await _configRepository.GetAllAsync();
        return configs.FirstOrDefault(c => c.IsActive) ?? configs.FirstOrDefault();
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

public record SetAutoConnectRequest(bool Enabled);

public record WriteGroupValueRequest(string Address, string? DatapointType, string? Value);

public record ReadGroupValueRequest(string Address);
