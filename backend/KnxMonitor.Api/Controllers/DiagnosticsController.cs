using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using KnxMonitor.Core.DTOs;
using KnxMonitor.Core.Interfaces;
using KnxMonitor.Infrastructure;
using KnxMonitor.Infrastructure.Logging;
using KnxMonitor.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnxMonitor.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DiagnosticsController : ControllerBase
{
    /// <summary>Default window of the availability report when the caller names no range.</summary>
    private const int DefaultAvailabilityDays = 7;

    /// <summary>Window written into the diagnostics zip — wide enough to cover a stale bug report.</summary>
    private const int BundledAvailabilityDays = 30;

    /// <summary>
    /// Bundled JSON must read like the HTTP responses, otherwise the same report shows up in two
    /// shapes: MVC's camelCase plus string enums here as well, so "MonitorDown" stays "MonitorDown"
    /// instead of turning into an opaque 2 in the file a reporter attaches to an issue.
    /// </summary>
    private static readonly JsonSerializerOptions BundleJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private readonly LogBuffer _logBuffer;
    private readonly IMonitorHeartbeatRepository _heartbeats;

    public DiagnosticsController(LogBuffer logBuffer, IMonitorHeartbeatRepository heartbeats)
    {
        _logBuffer = logBuffer;
        _heartbeats = heartbeats;
    }

    /// <summary>Recent log entries from the in-memory buffer, optionally filtered.</summary>
    [HttpGet("logs")]
    public ActionResult<IEnumerable<LogEntryDto>> GetLogs(
        [FromQuery] string? level = null,
        [FromQuery] string? search = null,
        [FromQuery] int limit = 500)
    {
        IEnumerable<LogEntryDto> entries = _logBuffer.Snapshot();

        if (!string.IsNullOrWhiteSpace(level) && !level.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            entries = entries.Where(e => e.Level.Equals(level, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            entries = entries.Where(e =>
                e.Message.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (e.Source?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        // Newest first, capped.
        var result = entries.Reverse().Take(Math.Clamp(limit, 1, 5000)).ToList();
        return Ok(result);
    }

    /// <summary>Availability of the recording: when did the monitor run, and was the bus link up.</summary>
    /// <remarks>
    /// Answers the question a hole in the telegram history cannot: a stretch reported as
    /// <c>Up</c> was genuinely quiet on the bus, <c>BusDown</c> means the link was gone, and
    /// <c>MonitorDown</c> means the process was not running (restart, standby, crash). Derived from
    /// the one-per-minute heartbeats, so boundaries are accurate to about one minute. Stretches the
    /// record does not cover — before the first beat was ever written, or past its retention —
    /// report as <c>Unknown</c> and are deliberately not counted as outages.
    /// <para>
    /// The range is clamped to now, because the future is neither up nor down.
    /// </para>
    /// </remarks>
    /// <param name="from">Start (UTC). Defaults to seven days before <paramref name="to"/>.</param>
    /// <param name="to">End (UTC). Defaults to now; a later value is clamped to now.</param>
    [HttpGet("availability")]
    public async Task<ActionResult<AvailabilityResponse>> GetAvailability(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to)
    {
        var ct = HttpContext.RequestAborted;
        var (rangeFrom, rangeTo) = ResolveAvailabilityRange(from, to, DefaultAvailabilityDays);
        return Ok(await BuildAvailabilityAsync(rangeFrom, rangeTo, ct));
    }

    /// <summary>Downloads a diagnostics zip: recent log files + a system-info.json.</summary>
    [HttpGet("download")]
    public async Task<IActionResult> Download()
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            // system info
            var info = new
            {
                version = AssemblyVersion(),
                os = RuntimeInformation.OSDescription,
                arch = RuntimeInformation.OSArchitecture.ToString(),
                framework = RuntimeInformation.FrameworkDescription,
                environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production",
                logLevel = Environment.GetEnvironmentVariable("KNX_LOG_LEVEL") ?? "Information",
                dataDir = AppPaths.DataDir,
                dbSizeBytes = System.IO.File.Exists(AppPaths.DbPath) ? new FileInfo(AppPaths.DbPath).Length : 0,
                utcNow = DateTimeOffset.UtcNow
            };
            WriteZipText(zip, "system-info.json", JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));

            // in-memory recent log
            var sb = new StringBuilder();
            foreach (var e in _logBuffer.Snapshot())
            {
                sb.Append(e.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"))
                  .Append(" [").Append(e.Level).Append("] ")
                  .Append(e.Source).Append(": ").AppendLine(e.Message);
                if (!string.IsNullOrEmpty(e.Exception)) sb.AppendLine(e.Exception);
            }
            WriteZipText(zip, "recent-log.txt", sb.ToString());

            // Availability report: turns "there is a hole in my history" into an answerable
            // question without a round of questions to the reporter.
            try
            {
                var (availFrom, availTo) = ResolveAvailabilityRange(null, null, BundledAvailabilityDays);
                var availability = await BuildAvailabilityAsync(availFrom, availTo, HttpContext.RequestAborted);
                WriteZipText(zip, "availability.json", JsonSerializer.Serialize(availability, BundleJsonOptions));
            }
            catch (Exception ex)
            {
                WriteZipText(zip, "availability.json", $"{{\"error\": {JsonSerializer.Serialize(ex.Message)}}}");
            }

            // rolled log files from disk
            if (Directory.Exists(AppPaths.LogsDir))
            {
                foreach (var file in Directory.GetFiles(AppPaths.LogsDir, "*.log"))
                {
                    try
                    {
                        var entry = zip.CreateEntry($"logs/{Path.GetFileName(file)}", CompressionLevel.Optimal);
                        using var es = entry.Open();
                        // shared read — the live sink may hold the file open.
                        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        fs.CopyTo(es);
                    }
                    catch { /* skip locked/unreadable file */ }
                }
            }
        }

        ms.Position = 0;
        var name = $"knx-diagnostics-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.zip";
        return File(ms, "application/zip", name);
    }

    /// <summary>App version (for support / bug reports).</summary>
    [HttpGet("/api/version")]
    [AllowAnonymous]
    public IActionResult Version() => Ok(new { version = AssemblyVersion() });

    private async Task<AvailabilityResponse> BuildAvailabilityAsync(DateTime from, DateTime to, CancellationToken ct)
    {
        var beats = await _heartbeats.GetRangeAsync(from, to, ct);
        // The beat before the window decides whether the run-up to the first beat was covered.
        var previous = await _heartbeats.GetLastBeforeAsync(from, ct);
        // Where the record begins: everything before it is unknown rather than an outage.
        var recordStart = await _heartbeats.GetEarliestTimestampAsync(ct);
        return AvailabilityCalculator.Build(
            beats, from, to, MonitorHeartbeatWorker.Interval, previous, recordStart);
    }

    private static (DateTime From, DateTime To) ResolveAvailabilityRange(DateTime? from, DateTime? to, int defaultDays)
    {
        var now = DateTime.UtcNow;
        var rangeTo = to?.ToUniversalTime() ?? now;
        var rangeFrom = from?.ToUniversalTime() ?? rangeTo.AddDays(-defaultDays);

        if (rangeFrom > rangeTo)
        {
            (rangeFrom, rangeTo) = (rangeTo, rangeFrom);
        }

        // Clamp after the swap, not before: clamping first would let a reversed range put the
        // future back into the result via rangeFrom.
        if (rangeTo > now)
        {
            rangeTo = now;
        }
        if (rangeFrom > rangeTo)
        {
            rangeFrom = rangeTo;
        }

        // Callers pass the archive filter range straight through, so bound the scan: beyond the
        // heartbeat retention there is nothing to read anyway.
        var earliestUseful = rangeTo.AddDays(-MonitorHeartbeatWorker.RetentionDays);
        if (rangeFrom < earliestUseful)
        {
            rangeFrom = earliestUseful;
        }

        return (rangeFrom, rangeTo);
    }

    private static string AssemblyVersion()
    {
        var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        return asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? asm.GetName().Version?.ToString()
            ?? "0.0.0";
    }

    private static void WriteZipText(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        s.Write(bytes, 0, bytes.Length);
    }
}
