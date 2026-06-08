using FluentAssertions;
using KnxMonitor.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KnxMonitor.Infrastructure.Tests;

public class WalInterceptorTests
{
    [Fact]
    public async Task Opened_connection_is_in_WAL_mode()
    {
        using var db = new SqliteTestDb();
        using var scope = db.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await ctx.Database.OpenConnectionAsync();
        try
        {
            var conn = ctx.Database.GetDbConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode;";
            var mode = (string)(await cmd.ExecuteScalarAsync())!;
            mode.Should().BeEquivalentTo("wal");
        }
        finally
        {
            await ctx.Database.CloseConnectionAsync();
        }
    }
}
