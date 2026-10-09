using Microsoft.EntityFrameworkCore;
using MTGPriceTracker.Server.Data;

namespace MTGPriceTracker.Server.BackgroundServices;

/// <summary>
/// Runs a few representative queries at startup so the first real request does not pay for
/// EF model building, query compilation, JIT and cold SQLite/OS page caches.
/// </summary>
public class DatabaseWarmupService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<DatabaseWarmupService> _logger;

    public DatabaseWarmupService(IServiceProvider services, ILogger<DatabaseWarmupService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Same shape as the Cards page: name search + ordered first page + count.
            var q = db.Cards.AsNoTracking().Where(c => EF.Functions.Like(c.Name, "%a%"));
            await q.CountAsync(ct);
            var page = await q.Include(c => c.Set).OrderBy(c => c.Name).Take(48).ToListAsync(ct);

            // Touch the price index the card grid uses for latest prices.
            var uuids = page.Select(c => c.Uuid).ToList();
            await db.PriceSnapshots.AsNoTracking()
                .Where(p => uuids.Contains(p.CardUuid))
                .Select(p => p.Price)
                .Take(1)
                .ToListAsync(ct);

            _logger.LogInformation("Database warm-up finished in {Ms} ms", sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Database warm-up failed (non-fatal)");
        }
    }
}
