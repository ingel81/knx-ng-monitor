using FluentAssertions;
using KnxMonitor.Core.Entities;
using KnxMonitor.Infrastructure.Data;
using KnxMonitor.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnxMonitor.Infrastructure.Tests;

/// <summary>
/// The cache normally skips a reload when the active project id has not changed. A re-import
/// keeps that id while rewriting names and DPTs, so it has to be able to force the reload (#24).
/// </summary>
public class ProjectCacheServiceTests
{
    private static ProjectCacheService Cache(SqliteTestDb db)
        => new(NullLogger<ProjectCacheService>.Instance,
               db.Services.GetRequiredService<IServiceScopeFactory>());

    private static async Task SeedActiveProjectAsync(SqliteTestDb db, string? dpt)
    {
        using var scope = db.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        context.Projects.Add(new Project
        {
            Name = "P",
            FileName = "p.knxproj",
            IsActive = true,
            EtsProjectId = "P-0600",
            GroupAddresses = { new GroupAddress { Address = "1/1/1", Name = "Licht", DatapointType = dpt } }
        });
        await context.SaveChangesAsync();
    }

    /// <summary>Simulates what a re-import does: same project row, new DPT on the same address.</summary>
    private static async Task SetDatapointTypeAsync(SqliteTestDb db, string dpt)
    {
        using var scope = db.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ga = context.GroupAddresses.Single(g => g.Address == "1/1/1");
        ga.DatapointType = dpt;
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Refresh_without_force_keeps_the_stale_entry_of_the_same_project()
    {
        using var db = new SqliteTestDb();
        await SeedActiveProjectAsync(db, dpt: null);

        var cache = Cache(db);
        await cache.InitializeAsync();

        await SetDatapointTypeAsync(db, "DPST-1-1");
        await cache.RefreshAsync();

        cache.GetByAddress("1/1/1")!.DatapointType.Should().BeNull();
    }

    [Fact]
    public async Task Refresh_with_force_picks_up_the_reimported_datapoint_type()
    {
        using var db = new SqliteTestDb();
        await SeedActiveProjectAsync(db, dpt: null);

        var cache = Cache(db);
        await cache.InitializeAsync();

        await SetDatapointTypeAsync(db, "DPST-1-1");
        await cache.RefreshAsync(force: true);

        cache.GetByAddress("1/1/1")!.DatapointType.Should().Be("DPST-1-1");
    }
}
