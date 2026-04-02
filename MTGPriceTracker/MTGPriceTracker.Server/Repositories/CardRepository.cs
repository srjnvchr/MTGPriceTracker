using Microsoft.EntityFrameworkCore;
using MTGPriceTracker.Server.Data;
using MTGPriceTracker.Server.Models;
using MTGPriceTracker.Server.Repositories.Interfaces;
using MTGPriceTracker.Shared.DTOs;

namespace MTGPriceTracker.Server.Repositories;

public class CardRepository : ICardRepository
{
    private readonly AppDbContext _db;

    public CardRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<Card>> SearchAsync(CardSearchQuery query, IEnumerable<string> favoriteUuids, CancellationToken ct = default)
    {
        var favoriteSet = favoriteUuids.ToHashSet();

        var q = _db.Cards
            .Include(c => c.Set)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Query))
        {
            var term = query.Query.Trim().ToLower();
            q = q.Where(c => c.Name.ToLower().Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(query.SetCode))
            q = q.Where(c => c.SetCode == query.SetCode);

        if (!string.IsNullOrWhiteSpace(query.Rarity))
            q = q.Where(c => c.Rarity.ToLower() == query.Rarity.ToLower());

        if (!string.IsNullOrWhiteSpace(query.Type))
            q = q.Where(c => c.Type.ToLower().Contains(query.Type.ToLower()));

        if (query.FavoritesOnly == true)
            q = q.Where(c => favoriteSet.Contains(c.Uuid));

        // Sorting
        q = query.SortBy?.ToLower() switch
        {
            "name" => query.SortDescending ? q.OrderByDescending(c => c.Name) : q.OrderBy(c => c.Name),
            "set" => query.SortDescending ? q.OrderByDescending(c => c.SetCode) : q.OrderBy(c => c.SetCode),
            "rarity" => query.SortDescending ? q.OrderByDescending(c => c.Rarity) : q.OrderBy(c => c.Rarity),
            _ => q.OrderBy(c => c.Name)
        };

        var totalCount = await q.CountAsync(ct);

        var items = await q
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PagedResult<Card>
        {
            Items = items,
            TotalCount = totalCount,
            Page = query.Page,
            PageSize = query.PageSize
        };
    }

    public async Task<Card?> GetByUuidAsync(string uuid, CancellationToken ct = default)
    {
        return await _db.Cards
            .Include(c => c.Set)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Uuid == uuid, ct);
    }

    public async Task<bool> ExistsAsync(string uuid, CancellationToken ct = default)
    {
        return await _db.Cards.AnyAsync(c => c.Uuid == uuid, ct);
    }

    public async Task UpsertAsync(Card card, CancellationToken ct = default)
    {
        var existing = await _db.Cards.FindAsync(new object[] { card.Uuid }, ct);
        if (existing is null)
            _db.Cards.Add(card);
        else
        {
            existing.Name = card.Name;
            existing.SetCode = card.SetCode;
            existing.Rarity = card.Rarity;
            existing.Type = card.Type;
            existing.ManaCost = card.ManaCost;
            existing.Text = card.Text;
            existing.FlavorText = card.FlavorText;
            existing.Power = card.Power;
            existing.Toughness = card.Toughness;
            existing.Loyalty = card.Loyalty;
            existing.Artist = card.Artist;
            existing.ScryfallId = card.ScryfallId;
            existing.ColorIdentity = card.ColorIdentity;
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task BulkUpsertAsync(IEnumerable<Card> cards, CancellationToken ct = default)
    {
        var cardList = cards.ToList();
        var uuids = cardList.Select(c => c.Uuid).ToHashSet();
        var existing = await _db.Cards
            .Where(c => uuids.Contains(c.Uuid))
            .ToDictionaryAsync(c => c.Uuid, ct);

        foreach (var card in cardList)
        {
            if (existing.TryGetValue(card.Uuid, out var dbCard))
            {
                dbCard.Name = card.Name;
                dbCard.SetCode = card.SetCode;
                dbCard.Rarity = card.Rarity;
                dbCard.Type = card.Type;
                dbCard.ManaCost = card.ManaCost;
                dbCard.Text = card.Text;
                dbCard.ScryfallId = card.ScryfallId;
                dbCard.ColorIdentity = card.ColorIdentity;
                dbCard.Artist = card.Artist;
                dbCard.Power = card.Power;
                dbCard.Toughness = card.Toughness;
                dbCard.Loyalty = card.Loyalty;
            }
            else
            {
                _db.Cards.Add(card);
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<int> GetTotalCountAsync(CancellationToken ct = default)
    {
        return await _db.Cards.CountAsync(ct);
    }

    public async Task<IEnumerable<string>> GetAllUuidsAsync(CancellationToken ct = default)
    {
        return await _db.Cards.Select(c => c.Uuid).ToListAsync(ct);
    }
}
