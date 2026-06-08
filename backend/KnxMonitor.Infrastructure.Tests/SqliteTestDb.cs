using KnxMonitor.Core.Interfaces;
using KnxMonitor.Infrastructure.Data;
using KnxMonitor.Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KnxMonitor.Infrastructure.Tests;

/// <summary>
/// Spins up a real file-backed SQLite database (so WAL works) wired through the same
/// DI registrations the app uses, with the schema created from the model.
/// </summary>
public sealed class SqliteTestDb : IDisposable
{
    private readonly string _path;
    public ServiceProvider Services { get; }

    public SqliteTestDb()
    {
        _path = Path.Combine(Path.GetTempPath(), $"knxtest-{Guid.NewGuid():N}.db");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlite($"Data Source={_path}")
                   .AddInterceptors(new SqliteWalConnectionInterceptor()));
        services.AddScoped<ITelegramRepository, TelegramRepository>();
        services.AddScoped<IRecordingSettingsRepository, RecordingSettingsRepository>();

        Services = services.BuildServiceProvider();

        using var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreated();
    }

    public IServiceScope CreateScope() => Services.CreateScope();

    public void Dispose()
    {
        Services.Dispose();
        SqliteConnection.ClearAllPools();
        TryDelete(_path);
        TryDelete(_path + "-wal");
        TryDelete(_path + "-shm");
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best effort */ }
    }
}
