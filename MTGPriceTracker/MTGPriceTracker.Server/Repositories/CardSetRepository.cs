using Microsoft.EntityFrameworkCore;
using MTGPriceTracker.Server.Data;
using MTGPriceTracker.Server.Models;
using MTGPriceTracker.Server.Repositories.Interfaces;

namespace MTGPriceTracker.Server.Repositories;

public class CardSetRepository : ICardSetRepository
{
    private readonly AppDbContext _db;

    public CardSetRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IEnumerable<CardSet>> GetAllAsync(CancellationToken ct = default)
    {
        return await _db.CardSets
            .AsNoTracking()
            .OrderByDescending(s => s.ReleaseDate)
            .ToListAsync(ct);
    }

    public async Task<CardSet?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        return await _db.CardSets
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Code == code, ct);
    }

    public async Task BulkUpsertAsync(IEnumerable<CardSet> sets, CancellationToken ct = default)
    {
        var setList = sets.ToList();
        var codes = setList.Select(s => s.Code).ToHashSet();
        var existing = await _db.CardSets
            .Where(s => codes.Contains(s.Code))
            .ToDictionaryAsync(s => s.Code, ct);

        foreach (var set in setList)
        {
            if (existing.TryGetValue(set.Code, out var dbSet))
            {
                dbSet.Name = set.Name;
                dbSet.ReleaseDate = set.ReleaseDate;
                dbSet.SetType = set.SetType;
            }
            else
            {
                _db.CardSets.Add(set);
            }
        }

        await _db.SaveChangesAsync(ct);
    }
}
