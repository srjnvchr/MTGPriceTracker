using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MTGPriceTracker.Server.BackgroundServices;
using MTGPriceTracker.Server.Data;
using MTGPriceTracker.Server.Models;
using MTGPriceTracker.Server.Repositories.Interfaces;

namespace MTGPriceTracker.Server.Repositories;

public class PriceRepository : IPriceRepository
{
    private static readonly TimeSpan LatestPriceCacheTtl = TimeSpan.FromMinutes(30);

    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly SyncState _syncState;

    public PriceRepository(AppDbContext db, IMemoryCache cache, SyncState syncState)
    {
        _db = db;
        _cache = cache;
        _syncState = syncState;
    }

    public async Task<IEnumerable<PriceSnapshot>> GetHistoryAsync(
        string cardUuid,
        string? vendor = null,
        int days = 90,
        CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-days);

        var q = _db.PriceSnapshots
            .AsNoTracking()
            .Where(p => p.CardUuid == cardUuid && p.Date >= cutoff);

        if (!string.IsNullOrWhiteSpace(vendor))
            q = q.Where(p => p.Vendor == vendor);

        return await q.OrderBy(p => p.Date).ToListAsync(ct);
    }

    public async Task<Dictionary<string, decimal>> GetLatestPricesAsync(string cardUuid, CancellationToken ct = default)
    {
        var map = await GetLatestPricesForCardsAsync(new[] { cardUuid }, ct);
        return map.TryGetValue(cardUuid, out var prices) ? prices : new Dictionary<string, decimal>();
    }

    /// <summary>
    /// Latest price per vendor / price type / condition for each card. Prices only change when a
    /// sync runs, so results are cached per card and versioned by the last sync. While a sync is
    /// running the cache is bypassed so half-imported data is never kept.
    /// </summary>
    public async Task<Dictionary<string, Dictionary<string, decimal>>> GetLatestPricesForCardsAsync(
        IEnumerable<string> cardUuids,
        CancellationToken ct = default)
    {
        var uuids = cardUuids.Distinct().ToList();
        var result = new Dictionary<string, Dictionary<string, decimal>>();
        if (uuids.Count == 0) return result;

        var useCache = !_syncState.IsRunning;
        var version = _syncState.LastSyncAt?.Ticks ?? 0;
        var missing = new List<string>();

        foreach (var uuid in uuids)
        {
            if (useCache && _cache.TryGetValue(CacheKey(version, uuid), out Dictionary<string, decimal>? hit) && hit is not null)
            {
                if (hit.Count > 0) result[uuid] = hit;
            }
            else
            {
                missing.Add(uuid);
            }
        }

        if (missing.Count == 0) return result;

        var fetched = await QueryLatestAsync(missing, ct);
        foreach (var uuid in missing)
        {
            var prices = fetched.TryGetValue(uuid, out var p) ? p : new Dictionary<string, decimal>();
            if (useCache) _cache.Set(CacheKey(version, uuid), prices, LatestPriceCacheTtl);
            if (prices.Count > 0) result[uuid] = prices;
        }

        return result;
    }

    private static string CacheKey(long version, string uuid) => $"latest-prices:{version}:{uuid}";

    /// <summary>
    /// Reads only the newest row of each price series. The correlated MAX(Date) is a single seek on
    /// the (CardUuid, Vendor, PriceType, Condition, Date) unique index, so the full price history of
    /// a card is never loaded (the old GroupBy/First() query read all of it and took minutes on a
    /// large database).
    /// </summary>
    private async Task<Dictionary<string, Dictionary<string, decimal>>> QueryLatestAsync(
        List<string> uuids,
        CancellationToken ct)
    {
        var result = new Dictionary<string, Dictionary<string, decimal>>();

        await _db.Database.OpenConnectionAsync(ct);
        try
        {
            var conn = _db.Database.GetDbConnection();

            foreach (var chunk in uuids.Chunk(500))
            {
                await using var cmd = conn.CreateCommand();

                var names = new string[chunk.Length];
                for (var i = 0; i < chunk.Length; i++)
                {
                    names[i] = $"@u{i}";
                    var prm = cmd.CreateParameter();
                    prm.ParameterName = names[i];
                    prm.Value = chunk[i];
                    cmd.Parameters.Add(prm);
                }

                cmd.CommandText = $@"
                    SELECT p.CardUuid, p.Vendor, p.PriceType, p.Condition, p.Price
                    FROM PriceSnapshots p
                    WHERE p.CardUuid IN ({string.Join(",", names)})
                      AND p.Date = (SELECT MAX(q.Date)
                                    FROM PriceSnapshots q
                                    WHERE q.CardUuid = p.CardUuid
                                      AND q.Vendor = p.Vendor
                                      AND q.PriceType = p.PriceType
                                      AND q.Condition IS p.Condition)";

                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var uuid = reader.GetString(0);
                    var vendor = reader.GetString(1);
                    var priceType = reader.GetString(2);
                    var condition = reader.IsDBNull(3) ? null : reader.GetString(3);
                    var price = Convert.ToDecimal(reader.GetValue(4), CultureInfo.InvariantCulture);

                    var key = condition != null ? $"{vendor}_{priceType}_{condition}" : $"{vendor}_{priceType}";

                    if (!result.TryGetValue(uuid, out var prices))
                        result[uuid] = prices = new Dictionary<string, decimal>();
                    prices[key] = price;
                }
            }
        }
        finally
        {
            await _db.Database.CloseConnectionAsync();
        }

        return result;
    }

    public async Task BulkUpsertAsync(IEnumerable<PriceSnapshot> snapshots, CancellationToken ct = default)
    {
        var snapshotList = snapshots.ToList();
        if (snapshotList.Count == 0) return;

        // Deduplicate within the batch — two GG products can parse to the same UUID+condition.
        var deduped = snapshotList
            .GroupBy(s => $"{s.CardUuid}|{s.Vendor}|{s.PriceType}|{s.Condition ?? ""}|{s.Date:yyyy-MM-dd}")
            .Select(g => g.Last())
            .ToList();

        // Use Microsoft.Data.Sqlite directly with a prepared statement.
        //
        // Why not EF Core ExecuteSqlAsync?
        //   Each call parses the interpolated SQL template, binds parameters, prepares a new
        //   SQLite statement object, executes it, and disposes it — plus an async state-machine
        //   transition per row. At 17 million rows that overhead alone takes hours.
        //
        // This approach instead:
        //   • Prepares the statement ONCE, then reuses it by swapping parameter values.
        //   • Uses synchronous ExecuteNonQuery in the inner loop — no per-row async overhead.
        //   • Commits every 50 000 rows — SQLite WAL + large transactions = maximum throughput.
        //   • Expected throughput: ~200 000–500 000 rows/second vs ~500 rows/second before.

        const int commitEvery = 50_000;

        var sqliteConn = (SqliteConnection)_db.Database.GetDbConnection();
        bool wasOpen = sqliteConn.State == System.Data.ConnectionState.Open;
        if (!wasOpen) await sqliteConn.OpenAsync(ct);

        SqliteTransaction? tx = null;
        try
        {
            tx = sqliteConn.BeginTransaction();

            using var cmd = sqliteConn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO "PriceSnapshots" ("CardUuid","Condition","Currency","Date","Price","PriceType","Vendor")
                VALUES ($uuid,$cond,$curr,$date,$price,$pt,$vendor)
                ON CONFLICT("CardUuid","Vendor","PriceType","Condition","Date") DO UPDATE SET
                    "Price"    = excluded."Price",
                    "Currency" = excluded."Currency"
                """;

            var pUuid  = cmd.Parameters.Add("$uuid",   SqliteType.Text);
            var pCond  = cmd.Parameters.Add("$cond",   SqliteType.Text);
            var pCurr  = cmd.Parameters.Add("$curr",   SqliteType.Text);
            var pDate  = cmd.Parameters.Add("$date",   SqliteType.Text);
            var pPrice = cmd.Parameters.Add("$price",  SqliteType.Real);
            var pPt    = cmd.Parameters.Add("$pt",     SqliteType.Text);
            var pVend  = cmd.Parameters.Add("$vendor", SqliteType.Text);

            cmd.Prepare();  // compile the statement once

            int rowCount = 0;
            foreach (var s in deduped)
            {
                ct.ThrowIfCancellationRequested();

                pUuid.Value  = s.CardUuid;
                pCond.Value  = (object?)s.Condition ?? DBNull.Value;
                pCurr.Value  = s.Currency;
                pDate.Value  = s.Date.ToString("yyyy-MM-dd");
                pPrice.Value = (double)s.Price;
                pPt.Value    = s.PriceType;
                pVend.Value  = s.Vendor;

                cmd.ExecuteNonQuery();  // synchronous — no async overhead per row
                rowCount++;

                if (rowCount % commitEvery == 0)
                {
                    await tx.CommitAsync(ct);
                    await tx.DisposeAsync();
                    tx = sqliteConn.BeginTransaction();
                    cmd.Transaction = tx;
                }
            }

            await tx.CommitAsync(ct);
        }
        finally
        {
            if (tx != null) await tx.DisposeAsync();
            if (!wasOpen) await sqliteConn.CloseAsync();
        }
    }

    /// <summary>
    /// Plain INSERT with no ON CONFLICT logic — use this for fresh bulk loads where
    /// the caller guarantees no duplicates (e.g. the one-time history backfill).
    /// Identical performance characteristics to BulkUpsertAsync but ~2x faster because
    /// there is no B-tree lookup for the unique index on every row.
    /// Call DropPriceSnapshotIndexesAsync before and RebuildPriceSnapshotIndexesAsync after
    /// for maximum throughput.
    /// </summary>
    public async Task BulkInsertAsync(IEnumerable<PriceSnapshot> snapshots, CancellationToken ct = default)
    {
        var snapshotList = snapshots.ToList();
        if (snapshotList.Count == 0) return;

        // Deduplicate in memory — the unique index is dropped during history imports so
        // we must enforce uniqueness ourselves before writing.
        var deduped = snapshotList
            .GroupBy(s => $"{s.CardUuid}|{s.Vendor}|{s.PriceType}|{s.Condition ?? ""}|{s.Date:yyyy-MM-dd}")
            .Select(g => g.Last())
            .ToList();

        const int commitEvery = 100_000;   // larger batches OK since no index maintenance

        var sqliteConn = (SqliteConnection)_db.Database.GetDbConnection();
        bool wasOpen = sqliteConn.State == System.Data.ConnectionState.Open;
        if (!wasOpen) await sqliteConn.OpenAsync(ct);

        SqliteTransaction? tx = null;
        try
        {
            tx = sqliteConn.BeginTransaction();

            using var cmd = sqliteConn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO "PriceSnapshots" ("CardUuid","Condition","Currency","Date","Price","PriceType","Vendor")
                VALUES ($uuid,$cond,$curr,$date,$price,$pt,$vendor)
                """;

            var pUuid  = cmd.Parameters.Add("$uuid",   SqliteType.Text);
            var pCond  = cmd.Parameters.Add("$cond",   SqliteType.Text);
            var pCurr  = cmd.Parameters.Add("$curr",   SqliteType.Text);
            var pDate  = cmd.Parameters.Add("$date",   SqliteType.Text);
            var pPrice = cmd.Parameters.Add("$price",  SqliteType.Real);
            var pPt    = cmd.Parameters.Add("$pt",     SqliteType.Text);
            var pVend  = cmd.Parameters.Add("$vendor", SqliteType.Text);

            cmd.Prepare();

            int rowCount = 0;
            foreach (var s in deduped)
            {
                ct.ThrowIfCancellationRequested();

                pUuid.Value  = s.CardUuid;
                pCond.Value  = (object?)s.Condition ?? DBNull.Value;
                pCurr.Value  = s.Currency;
                pDate.Value  = s.Date.ToString("yyyy-MM-dd");
                pPrice.Value = (double)s.Price;
                pPt.Value    = s.PriceType;
                pVend.Value  = s.Vendor;

                cmd.ExecuteNonQuery();
                rowCount++;

                if (rowCount % commitEvery == 0)
                {
                    await tx.CommitAsync(ct);
                    await tx.DisposeAsync();
                    tx = sqliteConn.BeginTransaction();
                    cmd.Transaction = tx;
                }
            }

            await tx.CommitAsync(ct);
        }
        finally
        {
            if (tx != null) await tx.DisposeAsync();
            if (!wasOpen) await sqliteConn.CloseAsync();
        }
    }

    public async Task DropPriceSnapshotIndexesAsync(CancellationToken ct = default)
    {
        // Drop all three indexes on PriceSnapshots before a bulk load.
        // SQLite must update every index B-tree on every INSERT — dropping them first
        // and rebuilding after gives ~2x throughput improvement for large imports.
        await _db.Database.ExecuteSqlRawAsync(
            """DROP INDEX IF EXISTS "IX_PriceSnapshots_CardUuid_Vendor_PriceType_Condition_Date" """, ct);
        await _db.Database.ExecuteSqlRawAsync(
            """DROP INDEX IF EXISTS "IX_PriceSnapshots_CardUuid" """, ct);
        await _db.Database.ExecuteSqlRawAsync(
            """DROP INDEX IF EXISTS "IX_PriceSnapshots_Date" """, ct);
    }

    public async Task RebuildPriceSnapshotIndexesAsync(CancellationToken ct = default)
    {
        await _db.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_PriceSnapshots_CardUuid_Vendor_PriceType_Condition_Date"
            ON "PriceSnapshots" ("CardUuid", "Vendor", "PriceType", "Condition", "Date")
            """, ct);
        await _db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_PriceSnapshots_CardUuid" ON "PriceSnapshots" ("CardUuid") """, ct);
        await _db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_PriceSnapshots_Date" ON "PriceSnapshots" ("Date") """, ct);
    }

    public async Task<int> GetTotalCountAsync(CancellationToken ct = default)
    {
        return await _db.PriceSnapshots.CountAsync(ct);
    }

    public async Task<DateTime?> GetLastSyncDateAsync(string vendor, CancellationToken ct = default)
    {
        var latest = await _db.PriceSnapshots
            .AsNoTracking()
            .Where(p => p.Vendor == vendor)
            .MaxAsync(p => (DateTime?)p.Date, ct);

        return latest;
    }

    public async Task<int> GetDistinctPriceDatesCountAsync(CancellationToken ct = default)
    {
        return await _db.PriceSnapshots
            .AsNoTracking()
            .Where(p => p.Vendor == "tcgplayer") // use one vendor as a representative sample
            .Select(p => p.Date.Date)
            .Distinct()
            .CountAsync(ct);
    }

    public async Task<int> DeleteByVendorAsync(string vendor, CancellationToken ct = default)
    {
        // ExecuteDeleteAsync is the EF Core 7+ bulk-delete — single DELETE statement, no entity tracking.
        return await _db.PriceSnapshots
            .Where(p => p.Vendor == vendor)
            .ExecuteDeleteAsync(ct);
    }
}
