using KnxMonitor.Core.Interfaces;
using KnxMonitor.Core.Enums;
using KnxMonitor.Core.Services;
using KnxMonitor.Infrastructure.Services;
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
    private readonly TelegramPersistenceService _persistence;
    private readonly TelegramArchiveService _archive;
    private readonly ILogger<KnxController> _logger;

    public KnxController(
        IKnxConnectionService knxService,
        IKnxConfigurationRepository configRepository,
        TelegramPersistenceService persistence,
        TelegramArchiveService archive,
        ILogger<KnxController> logger)
    {
        _knxService = knxService;
        _configRepository = configRepository;
        _persistence = persistence;
        _archive = archive;
        _logger = logger;
    }

    /// <summary>Current state of the KNX bus link.</summary>
    /// <remarks>
    /// Read-only — nothing is sent to the bus. Configuration is the configuration the live link
    /// was established with and is null while disconnected; it is not necessarily the stored
    /// configuration flagged active. ManualDisconnect is latched by a manual disconnect and keeps
    /// the auto-connect worker from reconnecting until the next connect clears it.
    /// </remarks>
    /// <returns>Connection flag, link state (Disconnected / Connecting / Connected), the
    /// manual-disconnect latch, the configuration currently in use, and recording health
    /// (link timestamps plus drop counters, both reset on restart).</returns>
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var config = await _knxService.GetActiveConfigurationAsync();
        return Ok(new
        {
            IsConnected = _knxService.IsConnected,
            State = _knxService.State.ToString(),
            ManualDisconnect = _knxService.ManualDisconnect,
            Configuration = config,
            // Recording health: when the link came up, when it last dropped, and whether anything
            // was thrown away because a writer could not keep up. Counters reset on restart.
            ConnectedSince = _knxService.ConnectedSince,
            LastDisconnectedAt = _knxService.LastDisconnectedAt,
            DroppedTelegrams = _persistence.DroppedCount,
            DroppedArchiveRecords = _archive.DroppedCount
        });
    }

    /// <summary>Opens the KNX bus link using the stored configuration with the given id.</summary>
    /// <remarks>
    /// Touches the installation: an IP tunnel is opened (or the routing multicast group joined),
    /// and received telegrams start flowing into the live view and the history again. An existing
    /// link is torn down first. Clears the manual-disconnect latch, so the auto-connect worker may
    /// keep the link up from here on. The request body is the bare configuration id (a JSON number).
    /// Unknown id yields 404, a failed connect 400.
    /// </remarks>
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

    /// <summary>Closes the KNX bus link.</summary>
    /// <remarks>
    /// Ends telegram recording until the next connect. Latches manual-disconnect so the
    /// auto-connect worker does not reconnect on its own; the next connect (or enabling
    /// auto-connect) clears the latch. Succeeds even when no link is currently up.
    /// </remarks>
    [HttpPost("disconnect")]
    public async Task<IActionResult> Disconnect()
    {
        await _knxService.DisconnectAsync();
        return Ok(new { Message = "Disconnected successfully" });
    }

    /// <summary>
    /// Probes whether a stored configuration can reach the bus. Sends no group telegram and
    /// changes nothing in the stored configuration.
    /// </summary>
    /// <remarks>
    /// The probe uses a throwaway connection. If a live link is currently up it is paused for the
    /// duration of the probe (freeing the gateway's tunnel slot) and restored afterwards, so
    /// recording has a short gap — the frontend warns before triggering this while live.
    /// For routing configurations the probe additionally listens up to five seconds for real bus
    /// traffic: joining a multicast group succeeds locally even when no telegram can ever arrive.
    /// That case is reported as JoinedWithoutTraffic and counts as a warning, not a failure
    /// (Success stays true). Unknown id yields 404.
    /// </remarks>
    /// <returns>Success flag (false only for Failed), the outcome and a human-readable message.</returns>
    [HttpPost("test-connection")]
    public async Task<IActionResult> TestConnection([FromBody] int configurationId)
    {
        var config = await _configRepository.GetByIdAsync(configurationId);
        if (config == null)
        {
            return NotFound("Configuration not found");
        }

        var outcome = await _knxService.TestConnectionAsync(config);
        return Ok(new
        {
            // JoinedWithoutTraffic is not a failure — the socket is fine, we just cannot confirm
            // that anything reaches it. The UI renders it as a warning with its own message.
            Success = outcome != ConnectionTestOutcome.Failed,
            Outcome = outcome,
            Message = outcome switch
            {
                ConnectionTestOutcome.Success => "Connection successful",
                ConnectionTestOutcome.JoinedWithoutTraffic =>
                    "Joined the multicast group, but no bus traffic was observed",
                _ => "Connection failed"
            }
        });
    }

    /// <summary>
    /// Auto-connect state for the effective configuration (active one, else the first).
    /// The active project is coupled to the bus link, so this governs whether it auto-connects.
    /// </summary>
    /// <remarks>
    /// Read-only. When no configuration is stored at all, Enabled is reported as true (the default
    /// for a configuration that has yet to be created).
    /// </remarks>
    /// <returns>The auto-connect flag and whether a bus link is currently up.</returns>
    [HttpGet("autoconnect")]
    public async Task<IActionResult> GetAutoConnect()
    {
        var config = await GetEffectiveConfigAsync();
        return Ok(new { Enabled = config?.AutoConnect ?? true, Connected = _knxService.IsConnected });
    }

    /// <summary>Enables or disables auto-connect for the effective configuration.</summary>
    /// <remarks>
    /// Applied immediately, not merely stored: enabling opens the bus link right away when none is
    /// up (which clears the manual-disconnect latch), disabling closes an existing link. A failed
    /// immediate connect is not reported as an error — the auto-connect worker retries on its next
    /// tick (roughly every ten seconds). 404 when no configuration is stored.
    /// </remarks>
    /// <returns>The stored flag and whether a bus link is up afterwards.</returns>
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
    /// <remarks>
    /// This has a physical effect: the addressed actuators act on the value. The value is encoded
    /// with the given datapoint type; without a DatapointType only a raw hex string ("0x01",
    /// "01 02") is accepted, anything else is rejected. Answers 400 with Success=false when the
    /// address is missing or unparsable, when no bus link is up, or when the value could not be
    /// encoded for the given DPT.
    /// </remarks>
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
    /// <remarks>
    /// Puts a telegram on the real installation, but changes no value — the addressed device
    /// answers with its current one. The answering GroupValueResponse arrives through the normal
    /// receive path and appears in the live view and the history; this endpoint itself only
    /// confirms that the request was sent, it does not return the value. Answers 400 with
    /// Success=false when the address is missing or unparsable, or when no bus link is up.
    /// </remarks>
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

    /// <summary>Stores a new KNX interface configuration.</summary>
    /// <remarks>
    /// Storing alone does not touch the bus: the entry is created inactive and no connection is
    /// opened here. Connect explicitly, or leave AutoConnect on — the auto-connect worker picks a
    /// configuration only while no link is up, preferring the active one, else the first with
    /// AutoConnect enabled. The payload carries no secure-tunnel flag, so that option stays off for
    /// new entries.
    /// </remarks>
    /// <returns>The stored configuration including its assigned id.</returns>
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

    /// <summary>Returns a single stored KNX interface configuration.</summary>
    /// <param name="id">Id of the stored configuration; 404 when it does not exist.</param>
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

    /// <summary>Lists all stored KNX interface configurations.</summary>
    [HttpGet("configurations")]
    public async Task<IActionResult> GetAllConfigurations()
    {
        var configs = await _configRepository.GetAllAsync();
        return Ok(configs);
    }

    /// <summary>Updates a stored KNX interface configuration; may re-establish the live bus link.</summary>
    /// <remarks>
    /// When the gateway endpoint actually changes (IP address, port or connection type), the
    /// configuration has AutoConnect enabled and a link is currently up, the running connection is
    /// re-opened with the new settings — that briefly interrupts recording. Changing only the
    /// physical address or the auto-connect flag does not trigger a reconnect. A failed reconnect
    /// is not an error: the response reports Reconnected=false and the auto-connect worker retries.
    /// The payload carries no secure-tunnel flag, so that setting stays as stored.
    /// </remarks>
    /// <param name="id">Id of the stored configuration; 404 when it does not exist.</param>
    /// <param name="request">The new values — same payload shape as when creating a configuration.</param>
    /// <returns>The stored configuration and whether the live link was re-established.</returns>
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
