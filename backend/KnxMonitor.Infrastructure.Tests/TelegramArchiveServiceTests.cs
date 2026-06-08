using FluentAssertions;
using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Enums;
using KnxMonitor.Core.Interfaces;
using KnxMonitor.Core.Services;
using KnxMonitor.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnxMonitor.Infrastructure.Tests;

public class TelegramArchiveServiceTests : IDisposable
{
    private readonly string _dir;
    private readonly string _archiveDir;
    private DateTime _now = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);

    public TelegramArchiveServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"knxarchive-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _archiveDir = Path.Combine(_dir, "archive");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private (TelegramArchiveService Svc, FakeKnx Knx, FakeSettings Settings) Create(
        bool archiveEnabled, int? retentionDays = null)
    {
        var knx = new FakeKnx();
        var settings = new FakeSettings
        {
            Current = new RecordingSettings { ArchiveEnabled = archiveEnabled, ArchiveRetentionDays = retentionDays }
        };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={Path.Combine(_dir, "db.sqlite")}"
            })
            .Build();

        var svc = new TelegramArchiveService(
            knx, settings, config, NullLogger<TelegramArchiveService>.Instance, () => _now);
        return (svc, knx, settings);
    }

    private static KnxTelegram Tg(string dst = "1/1/1", MessageType mt = MessageType.Write)
        => new()
        {
            Timestamp = new DateTime(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc),
            DestinationAddress = dst,
            SourceAddress = "1.1.1",
            MessageType = mt,
            Value = new byte[] { 0x01 }
        };

    [Fact]
    public async Task Does_not_write_when_archive_disabled()
    {
        var (svc, knx, _) = Create(archiveEnabled: false);

        await svc.StartAsync(default);
        knx.Raise(Tg());
        await svc.StopAsync(default);

        if (Directory.Exists(_archiveDir))
        {
            Directory.GetFiles(_archiveDir).Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Writes_compact_ndjson_line_when_enabled()
    {
        var (svc, knx, _) = Create(archiveEnabled: true);

        await svc.StartAsync(default);
        knx.Raise(Tg(dst: "5/1/10", mt: MessageType.Write));
        await svc.StopAsync(default);

        var files = Directory.GetFiles(_archiveDir, "*.ndjson");
        files.Should().ContainSingle();
        Path.GetFileName(files[0]).Should().Be("2026-06-08.ndjson");

        var content = File.ReadAllText(files[0]);
        content.Should().Contain("\"d\":\"5/1/10\"");
        content.Should().Contain("\"mt\":\"Write\"");
        content.Should().Contain("\"r\":\"01\"");
    }

    [Fact]
    public async Task Startup_self_heals_stale_plain_day_files()
    {
        Directory.CreateDirectory(_archiveDir);
        File.WriteAllText(Path.Combine(_archiveDir, "2020-01-01.ndjson"), "{\"t\":\"x\"}\n");
        _now = new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        var (svc, _, _) = Create(archiveEnabled: false);
        await svc.StartAsync(default);
        await svc.StopAsync(default);

        File.Exists(Path.Combine(_archiveDir, "2020-01-01.ndjson.gz")).Should().BeTrue();
        File.Exists(Path.Combine(_archiveDir, "2020-01-01.ndjson")).Should().BeFalse();
    }

    [Fact]
    public async Task Retention_deletes_files_older_than_cutoff()
    {
        Directory.CreateDirectory(_archiveDir);
        File.WriteAllText(Path.Combine(_archiveDir, "2020-01-01.ndjson.gz"), "ignored");
        _now = new DateTime(2020, 1, 10, 0, 0, 0, DateTimeKind.Utc);

        var (svc, _, _) = Create(archiveEnabled: false, retentionDays: 1);
        await svc.StartAsync(default);
        await svc.StopAsync(default);

        File.Exists(Path.Combine(_archiveDir, "2020-01-01.ndjson.gz")).Should().BeFalse();
    }

    private sealed class FakeKnx : IKnxConnectionService
    {
        public event EventHandler<KnxTelegram>? TelegramReceived;
        public bool IsConnected => false;
        public Task<bool> ConnectAsync(KnxConfiguration configuration) => Task.FromResult(false);
        public Task DisconnectAsync() => Task.CompletedTask;
        public Task<KnxConfiguration?> GetActiveConfigurationAsync() => Task.FromResult<KnxConfiguration?>(null);
        public void Raise(KnxTelegram telegram) => TelegramReceived?.Invoke(this, telegram);
    }

    private sealed class FakeSettings : IRecordingSettingsProvider
    {
        public RecordingSettings Current { get; set; } = new();
        public int HotBufferMaxCount => Current.HotBufferMaxCount;
        public bool ArchiveEnabled => Current.ArchiveEnabled;
        public int? ArchiveRetentionDays => Current.ArchiveRetentionDays;
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(RecordingSettings updated, CancellationToken ct = default)
        {
            Current = updated;
            return Task.CompletedTask;
        }
    }
}
