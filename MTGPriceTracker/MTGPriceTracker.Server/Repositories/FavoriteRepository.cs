using Microsoft.EntityFrameworkCore;
using MTGPriceTracker.Server.Data;
using MTGPriceTracker.Server.Models;
using MTGPriceTracker.Server.Repositories.Interfaces;

namespace MTGPriceTracker.Server.Repositories;

public class FavoriteRepository : IFavoriteRepository
{
    private readonly AppDbContext _db;

    public FavoriteRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IEnumerable<UserFavorite>> GetAllAsync(CancellationToken ct = default)
    {
        return await _db.UserFavorites
            .Include(f => f.Card)
            .ThenInclude(c => c.Set)
            .AsNoTracking()
            .OrderByDescending(f => f.AddedAt)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<string>> GetAllUuidsAsync(CancellationToken ct = default)
    {
        return await _db.UserFavorites
            .AsNoTracking()
            .Select(f => f.CardUuid)
            .ToListAsync(ct);
    }

    public async Task<bool> IsFavoriteAsync(string cardUuid, CancellationToken ct = default)
    {
        return await _db.UserFavorites.AnyAsync(f => f.CardUuid == cardUuid, ct);
    }

    public async Task AddAsync(string cardUuid, CancellationToken ct = default)
    {
        var exists = await _db.UserFavorites.AnyAsync(f => f.CardUuid == cardUuid, ct);
        if (!exists)
        {
            _db.UserFavorites.Add(new UserFavorite
            {
                CardUuid = cardUuid,
                AddedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task RemoveAsync(string cardUuid, CancellationToken ct = default)
    {
        var favorite = await _db.UserFavorites
            .FirstOrDefaultAsync(f => f.CardUuid == cardUuid, ct);

        if (favorite is not null)
        {
            _db.UserFavorites.Remove(favorite);
            await _db.SaveChangesAsync(ct);
        }
    }
}
