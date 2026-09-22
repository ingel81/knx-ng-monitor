using FluentAssertions;
using KnxMonitor.Core.Interfaces;
using KnxMonitor.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KnxMonitor.Infrastructure.Tests;

public class ChartSelectionRepositoryTests
{
    [Fact]
    public async Task Upsert_creates_and_keeps_address_order()
    {
        using var db = new SqliteTestDb();
        using var scope = db.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IChartSelectionRepository>();

        var saved = await repo.UpsertByNameAsync("Bewässerung", new[] { "7/0/91", "7/0/90" });

        saved.Id.Should().BeGreaterThan(0);
        saved.Addresses.Should().Be("7/0/91,7/0/90");
        saved.UpdatedAt.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task Upsert_with_same_name_in_other_case_overwrites_instead_of_duplicating()
    {
        using var db = new SqliteTestDb();

        int firstId;
        using (var scope = db.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IChartSelectionRepository>();
            firstId = (await repo.UpsertByNameAsync("pumpe", new[] { "7/0/90" })).Id;
        }

        using (var scope = db.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IChartSelectionRepository>();
            var second = await repo.UpsertByNameAsync("Pumpe", new[] { "7/0/90", "7/0/91" });

            second.Id.Should().Be(firstId);
            second.Name.Should().Be("Pumpe", "the latest spelling wins");
            second.Addresses.Should().Be("7/0/90,7/0/91");

            var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await ctx.ChartSelections.CountAsync()).Should().Be(1);
        }
    }

    [Fact]
    public async Task GetAllOrdered_sorts_by_name_ignoring_case()
    {
        using var db = new SqliteTestDb();
        using var scope = db.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IChartSelectionRepository>();

        await repo.UpsertByNameAsync("Zimmer", new[] { "1/1/1" });
        await repo.UpsertByNameAsync("bad", new[] { "1/1/2" });
        await repo.UpsertByNameAsync("Außen", new[] { "1/1/3" });

        var all = await repo.GetAllOrderedAsync();

        all.Select(s => s.Name).Should().Equal("Außen", "bad", "Zimmer");
    }
}
