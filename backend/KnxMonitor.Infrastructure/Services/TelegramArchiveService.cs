using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using KnxMonitor.Core.DTOs;
using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Enums;
using KnxMonitor.Core.Interfaces;
using KnxMonitor.Core.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KnxMonitor.Infrastructure.Services;

/// <summary>
/// Cold-tier archive writer. Tees off <see cref="IKnxConnectionService.TelegramReceived"/> and
/// appends every telegram (when enabled) to a per-UTC-day NDJSON file. At day rollover the finished
/// day is gzipped; the current day stays plain (append-only, crash-safe). Opt-in via RecordingSettings.
/// </summary>
public class TelegramArchiveService : IHostedService, IAsyncDisposable
{
    private const int BufferCapacity = 100_000;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private const string DateFormat = "yyyy-MM-dd";

    private static readonly JsonSerializerOptions LineJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IKnxConnectionService _knxService;
    private readonly IRecordingSettingsProvider _settings;
    private readonly ILogger<TelegramArchiveService> _logger;
    private readonly Func<DateTime> _clock;
    private readonly string _archiveDir;

    private readonly Channel<ArchiveRecord> _buffer;
    private readonly CancellationTokenSource _cts = new();
    private Task? _writerTask;

    private FileStream? _stream;
    private StreamWriter? _writer;
    private DateTime _currentDay;
    private DateTime _lastFlush = DateTime.MinValue;
    private bool _dirty;

    private long _droppedTotal;
    private long _droppedLogged;

    public TelegramArchiveService(
        IKnxConnectionService knxService,
        IRecordingSettingsProvider settings,
        IConfiguration configuration,
        ILogger<TelegramArchiveService> logger,
        Func<DateTime>? clock = null)
    {
        _knxService = knxService;
        _settings = settings;
        _logger = logger;
        _clock = clock ?? (() => DateTime.UtcNow);
        _archiveDir = ResolveArchiveDir(configuration);

        _buffer = Channel.CreateBounded<ArchiveRecord>(new BoundedChannelOptions(BufferCapacity)
        {
            // Wait-mode + TryWrite lets us detect overflow (TryWrite returns false when full)
            // and count drops instead of silently losing telegrams.
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public string ArchiveDirectory => _archiveDir;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_archiveDir);
        try
        {
            GzipStalePlainFiles();
            RunRetentionSweep();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Archive startup maintenance failed");
        }

        _knxService.TelegramReceived += OnTelegramReceived;
        _writerTask = Task.Run(() => RunWriterAsync(_cts.Token));
        _logger.LogInformation("Telegram archive service started (dir: {Dir})", _archiveDir);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _knxService.TelegramReceived -= OnTelegramReceived;
        _buffer.Writer.TryComplete();
        _cts.Cancel();

        if (_writerTask != null)
        {
            try { await _writerTask.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) { }
        }

        CloseCurrentWriter(gzip: false); // current day stays plain
        _logger.LogInformation("Telegram archive service stopped");
    }

    // --- Hot path (event thread): synchronous and exception-proof so it can never break
    //     the multicast broadcast handler that shares this event. ---
    private void OnTelegramReceived(object? sender, KnxTelegram telegram)
    {
        try
        {
            if (!_settings.ArchiveEnabled)
            {
                return;
            }

            var record = new ArchiveRecord(
                telegram.Timestamp,
                telegram.SourceAddress,
                telegram.DestinationAddress,
                telegram.MessageType,
                Convert.ToHexString(telegram.Value),
                telegram.ValueDecoded);

            if (!_buffer.Writer.TryWrite(record))
            {
                Interlocked.Increment(ref _droppedTotal);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Archive enqueue failed");
        }
    }

    private async Task RunWriterAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(PollInterval);
            while (await timer.WaitForNextTickAsync(ct))
            {
                RolloverIfNeeded();
                DrainAndWrite();
                MaybeFlush();
                LogDropsIfAny();
            }
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
        finally
        {
            // Final drain + flush so we don't lose buffered records on shutdown.
            try
            {
                DrainAndWrite();
                if (_writer != null)
                {
                    _writer.Flush();
                    _stream?.Flush(true);
                    _dirty = false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Archive final flush failed");
            }
        }
    }

    private void DrainAndWrite()
    {
        var wrote = false;
        while (_buffer.Reader.TryRead(out var record))
        {
            try
            {
                EnsureWriter();
                _writer!.WriteLine(Serialize(record));
                wrote = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Archive write failed");
                break;
            }
        }
        if (wrote)
        {
            _dirty = true;
        }
    }

    private void MaybeFlush()
    {
        if (_dirty && _clock() - _lastFlush >= FlushInterval)
        {
            try
            {
                _writer?.Flush();
                _stream?.Flush(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Archive flush failed");
            }
            _dirty = false;
            _lastFlush = _clock();
        }
    }

    private void EnsureWriter()
    {
        if (_writer != null)
        {
            return;
        }

        _currentDay = _clock().Date;
        var path = PlainPath(_currentDay);
        _stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(_stream, new UTF8Encoding(false));
        _lastFlush = _clock();
    }

    private void RolloverIfNeeded()
    {
        if (_writer == null)
        {
            return;
        }
        if (_currentDay == _clock().Date)
        {
            return;
        }

        CloseCurrentWriter(gzip: true);
        try
        {
            RunRetentionSweep();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Archive retention sweep failed");
        }
    }

    private void CloseCurrentWriter(bool gzip)
    {
        if (_writer == null)
        {
            return;
        }

        var day = _currentDay;
        try
        {
            _writer.Flush();
            _stream?.Flush(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Archive close-flush failed");
        }

        _writer.Dispose();
        _stream?.Dispose();
        _writer = null;
        _stream = null;
        _dirty = false;

        if (gzip)
        {
            GzipDayFile(day);
        }
    }

    // --- File maintenance ---

    private void GzipStalePlainFiles()
    {
        var today = _clock().Date;
        foreach (var file in Directory.EnumerateFiles(_archiveDir, "*.ndjson"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (TryParseDay(name, out var day) && day < today)
            {
                GzipDayFile(day);
            }
        }
    }

    private void GzipDayFile(DateTime day)
    {
        var plain = PlainPath(day);
        if (!File.Exists(plain))
        {
            return;
        }

        var gz = GzipPath(day);
        try
        {
            using (var input = new FileStream(plain, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(gz, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
            {
                input.CopyTo(gzip);
            }
            // Only remove the raw file once the gzip is fully written and closed.
            File.Delete(plain);
            _logger.LogInformation("Archived {Day} → {File}", day.ToString(DateFormat), Path.GetFileName(gz));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to gzip archive day {Day}", day.ToString(DateFormat));
        }
    }

    private void RunRetentionSweep()
    {
        var retentionDays = _settings.ArchiveRetentionDays;
        if (retentionDays is null or <= 0)
        {
            return;
        }

        var cutoff = _clock().Date.AddDays(-retentionDays.Value);
        foreach (var file in Directory.EnumerateFiles(_archiveDir))
        {
            var name = Path.GetFileName(file);
            var datePart = name.EndsWith(".ndjson.gz", StringComparison.OrdinalIgnoreCase)
                ? name[..^".ndjson.gz".Length]
                : name.EndsWith(".ndjson", StringComparison.OrdinalIgnoreCase)
                    ? name[..^".ndjson".Length]
                    : null;

            if (datePart != null && TryParseDay(datePart, out var day) && day < cutoff)
            {
                try
                {
                    File.Delete(file);
                    _logger.LogInformation("Deleted expired archive file {File}", name);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to delete expired archive file {File}", name);
                }
            }
        }
    }

    private void LogDropsIfAny()
    {
        var total = Interlocked.Read(ref _droppedTotal);
        if (total > _droppedLogged)
        {
            _logger.LogWarning("Archive buffer overflow: {Count} telegrams dropped (cumulative)", total);
            _droppedLogged = total;
        }
    }

    // --- Controller-facing helpers (centralize path logic; safe alongside the writer) ---

    public IReadOnlyList<ArchiveDayDto> GetArchiveDays()
    {
        if (!Directory.Exists(_archiveDir))
        {
            return Array.Empty<ArchiveDayDto>();
        }

        var days = new List<ArchiveDayDto>();
        foreach (var file in Directory.EnumerateFiles(_archiveDir))
        {
            var name = Path.GetFileName(file);
            bool compressed;
            string? datePart;
            if (name.EndsWith(".ndjson.gz", StringComparison.OrdinalIgnoreCase))
            {
                compressed = true;
                datePart = name[..^".ndjson.gz".Length];
            }
            else if (name.EndsWith(".ndjson", StringComparison.OrdinalIgnoreCase))
            {
                compressed = false;
                datePart = name[..^".ndjson".Length];
            }
            else
            {
                continue;
            }

            if (!TryParseDay(datePart, out _))
            {
                continue;
            }

            days.Add(new ArchiveDayDto
            {
                Date = datePart,
                FileName = name,
                SizeBytes = new FileInfo(file).Length,
                Compressed = compressed
            });
        }

        return days.OrderByDescending(d => d.Date).ToList();
    }

    /// <summary>Opens an archive day for download. Prefers the gzipped file, falls back to plain (today).</summary>
    public (Stream Stream, string FileName, string ContentType)? OpenArchiveDay(string date)
    {
        if (!TryParseDay(date, out var day))
        {
            return null;
        }

        var gz = GzipPath(day);
        if (File.Exists(gz))
        {
            return (new FileStream(gz, FileMode.Open, FileAccess.Read, FileShare.Read),
                Path.GetFileName(gz), "application/gzip");
        }

        var plain = PlainPath(day);
        if (File.Exists(plain))
        {
            return (new FileStream(plain, FileMode.Open, FileAccess.Read, FileShare.ReadWrite),
                Path.GetFileName(plain), "application/x-ndjson");
        }

        return null;
    }

    private string PlainPath(DateTime day) => Path.Combine(_archiveDir, day.ToString(DateFormat) + ".ndjson");
    private string GzipPath(DateTime day) => Path.Combine(_archiveDir, day.ToString(DateFormat) + ".ndjson.gz");

    private static bool TryParseDay(string value, out DateTime day) =>
        DateTime.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out day);

    private static string Serialize(ArchiveRecord r) => JsonSerializer.Serialize(new ArchiveLine
    {
        T = r.Timestamp.ToString("O"),
        S = r.Source,
        D = r.Dest,
        Mt = r.Type.ToString(),
        R = r.Value,
        V = r.Decoded
    }, LineJsonOptions);

    private static string ResolveArchiveDir(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        var dataDir = "data";
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            try
            {
                var dbPath = new SqliteConnectionStringBuilder(connectionString).DataSource;
                var dir = Path.GetDirectoryName(dbPath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    dataDir = dir;
                }
            }
            catch
            {
                // fall back to ./data
            }
        }
        return Path.Combine(dataDir, "archive");
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _buffer.Writer.TryComplete();
        if (_writerTask != null)
        {
            try { await _writerTask; } catch { /* ignore */ }
        }
        CloseCurrentWriter(gzip: false);
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }

    private readonly record struct ArchiveRecord(
        DateTime Timestamp, string Source, string Dest, MessageType Type, string Value, string? Decoded);

    private sealed class ArchiveLine
    {
        [JsonPropertyName("t")] public string T { get; set; } = string.Empty;
        [JsonPropertyName("s")] public string S { get; set; } = string.Empty;
        [JsonPropertyName("d")] public string D { get; set; } = string.Empty;
        [JsonPropertyName("mt")] public string Mt { get; set; } = string.Empty;
        [JsonPropertyName("r")] public string R { get; set; } = string.Empty;
        [JsonPropertyName("v")] public string? V { get; set; }
    }
}
