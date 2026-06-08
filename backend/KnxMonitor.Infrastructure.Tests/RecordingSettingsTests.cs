using FluentAssertions;
using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Interfaces;
using KnxMonitor.Infrastructure.Data;
using KnxMonitor.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnxMonitor.Infrastructure.Tests;

public class RecordingSettingsTests
{
    [Fact]
    public async Task GetOrCreateAsync_seeds_defaults_exactly_once()
    {
        using var db = new SqliteTestDb();

        using (var scope = db.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IRecordingSettingsRepository>();
            var first = await repo.GetOrCreateAsync();
            first.HotBufferMaxCount.Should().Be(1_000_000);
            first.ArchiveEnabled.Should().BeFalse();
            first.ArchiveRetentionDays.Should().BeNull();
        }

        using (var scope = db.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IRecordingSettingsRepository>();
            await repo.GetOrCreateAsync();

            var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await ctx.RecordingSettings.CountAsync()).Should().Be(1);
        }
    }

    [Fact]
    public async Task Provider_initializes_and_applies_updates_live()
    {
        using var db = new SqliteTestDb();
        var scopeFactory = db.Services.GetRequiredService<IServiceScopeFactory>();
        var provider = new RecordingSettingsProvider(scopeFactory, NullLogger<RecordingSettingsProvider>.Instance);

        await provider.InitializeAsync();
        provider.HotBufferMaxCount.Should().Be(1_000_000);
        provider.ArchiveEnabled.Should().BeFalse();

        await provider.UpdateAsync(new RecordingSettings
        {
            HotBufferMaxCount = 500,
            ArchiveEnabled = true,
            ArchiveRetentionDays = 7
        });

        provider.HotBufferMaxCount.Should().Be(500);
        provider.ArchiveEnabled.Should().BeTrue();
        provider.ArchiveRetentionDays.Should().Be(7);

        // Persisted to the database
        using var scope = db.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRecordingSettingsRepository>();
        var stored = await repo.GetOrCreateAsync();
        stored.HotBufferMaxCount.Should().Be(500);
        stored.ArchiveEnabled.Should().BeTrue();
        stored.ArchiveRetentionDays.Should().Be(7);
    }
}
