using System.Data.Common;
using Microsoft.Data.Sqlite;
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
        // EF checks whether the database exists over a read-only connection. Switching journal
        // mode there fails with "attempt to write a readonly database" when the file is still
        // empty (0 bytes), which aborted startup. A read-only connection has nothing to set.
        if (connection is SqliteConnection sqlite
            && new SqliteConnectionStringBuilder(sqlite.ConnectionString).Mode == SqliteOpenMode.ReadOnly)
        {
            return;
        }

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
        command.ExecuteNonQuery();
    }
}
