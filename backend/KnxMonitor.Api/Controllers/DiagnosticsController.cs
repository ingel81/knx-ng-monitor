using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using KnxMonitor.Core.DTOs;
using KnxMonitor.Infrastructure;
using KnxMonitor.Infrastructure.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnxMonitor.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DiagnosticsController : ControllerBase
{
    private readonly LogBuffer _logBuffer;

    public DiagnosticsController(LogBuffer logBuffer)
    {
        _logBuffer = logBuffer;
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

    /// <summary>Downloads a diagnostics zip: recent log files + a system-info.json.</summary>
    [HttpGet("download")]
    public IActionResult Download()
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
