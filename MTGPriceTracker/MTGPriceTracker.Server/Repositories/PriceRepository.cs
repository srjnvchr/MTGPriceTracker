using Microsoft.EntityFrameworkCore;
using MTGPriceTracker.Server.Data;
using MTGPriceTracker.Server.Models;
using MTGPriceTracker.Server.Repositories.Interfaces;

namespace MTGPriceTracker.Server.Repositories;

public class PriceRepository : IPriceRepository
{
    private readonly AppDbContext _db;

    public PriceRepository(AppDbContext db)
    {
        _db = db;
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
        // For each vendor+priceType combo, get the most recent price
        var latestPerVendor = await _db.PriceSnapshots
            .AsNoTracking()
            .Where(p => p.CardUuid == cardUuid)
            .GroupBy(p => new { p.Vendor, p.PriceType })
            .Select(g => g.OrderByDescending(p => p.Date).First())
            .ToListAsync(ct);

        return latestPerVendor.ToDictionary(
            p => $"{p.Vendor}_{p.PriceType}",
            p => p.Price);
    }

    public async Task<Dictionary<string, Dictionary<string, decimal>>> GetLatestPricesForCardsAsync(
        IEnumerable<string> cardUuids,
        CancellationToken ct = default)
    {
        var uuidList = cardUuids.ToList();

        // Get latest price date per card+vendor+priceType combo
        var latestPrices = await _db.PriceSnapshots
            .AsNoTracking()
            .Where(p => uuidList.Contains(p.CardUuid))
            .GroupBy(p => new { p.CardUuid, p.Vendor, p.PriceType })
            .Select(g => g.OrderByDescending(p => p.Date).First())
            .ToListAsync(ct);

        var result = new Dictionary<string, Dictionary<string, decimal>>();
        foreach (var price in latestPrices)
        {
            if (!result.ContainsKey(price.CardUuid))
                result[price.CardUuid] = new Dictionary<string, decimal>();

            result[price.CardUuid][$"{price.Vendor}_{price.PriceType}"] = price.Price;
        }

        return result;
    }

    public async Task BulkUpsertAsync(IEnumerable<PriceSnapshot> snapshots, CancellationToken ct = default)
    {
        var snapshotList = snapshots.ToList();
        if (snapshotList.Count == 0) return;

        // Build lookup keys for existing records
        var keys = snapshotList.Select(s => new { s.CardUuid, s.Vendor, s.PriceType, s.Date }).ToList();

        // Check which ones already exist by querying for the date range
        var minDate = snapshotList.Min(s => s.Date);
        var maxDate = snapshotList.Max(s => s.Date);

        var existingLookup = await _db.PriceSnapshots
            .Where(p => p.Date >= minDate && p.Date <= maxDate)
            .ToListAsync(ct);

        var existingSet = existingLookup
            .ToDictionary(p => $"{p.CardUuid}|{p.Vendor}|{p.PriceType}|{p.Date:yyyy-MM-dd}");

        var toAdd = new List<PriceSnapshot>();

        foreach (var snapshot in snapshotList)
        {
            var key = $"{snapshot.CardUuid}|{snapshot.Vendor}|{snapshot.PriceType}|{snapshot.Date:yyyy-MM-dd}";
            if (existingSet.TryGetValue(key, out var existing))
            {
                existing.Price = snapshot.Price;
                existing.Currency = snapshot.Currency;
            }
            else
            {
                toAdd.Add(snapshot);
            }
        }

        if (toAdd.Count > 0)
            await _db.PriceSnapshots.AddRangeAsync(toAdd, ct);

        await _db.SaveChangesAsync(ct);
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
}
