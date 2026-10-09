using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MTGPriceTracker.Server.Data;

/// <summary>
/// Applies per-connection SQLite tuning. These pragmas are not persisted in the DB file,
/// so they must be set every time a connection opens.
/// </summary>
public class SqliteTuningInterceptor : DbConnectionInterceptor
{
    private const string Pragmas =
        "PRAGMA cache_size=-65536;" +      // 64 MB page cache (default is ~2 MB)
        "PRAGMA mmap_size=268435456;" +    // memory-map up to 256 MB for faster cold reads
        "PRAGMA temp_store=MEMORY;";       // sorts/temp indexes in RAM, not on disk

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        => Apply(connection);

    public override Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Apply(connection);
        return Task.CompletedTask;
    }

    private static void Apply(DbConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = Pragmas;
        cmd.ExecuteNonQuery();
    }
}
