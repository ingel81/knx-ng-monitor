using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace KnxMonitor.Infrastructure.Data;

/// <summary>
/// Enables SQLite WAL mode (and NORMAL sync) on every opened connection.
/// WAL is persistent at the DB-file level, but applying per-open is idempotent and
/// also (re)applies <c>synchronous</c>. This is the robust place to set PRAGMAs given
/// the DbContext is built from pooled options (no OnConfiguring).
/// </summary>
public sealed class SqliteWalConnectionInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        Apply(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Apply(connection);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    private static void Apply(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
        command.ExecuteNonQuery();
    }
}
