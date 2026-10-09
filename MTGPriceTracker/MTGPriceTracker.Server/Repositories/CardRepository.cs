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
            // SQLite LIKE is case-insensitive for ASCII, so no per-row lower() is needed.
            var term = EscapeLike(query.Query.Trim());
            q = q.Where(c => EF.Functions.Like(c.Name, $"%{term}%", "\\"));
        }

        if (!string.IsNullOrWhiteSpace(query.SetCode))
            q = q.Where(c => c.SetCode == query.SetCode);

        if (!string.IsNullOrWhiteSpace(query.Rarity))
            q = q.Where(c => c.Rarity == query.Rarity.ToLower()); // stored lowercase — keeps the Rarity index usable

        if (!string.IsNullOrWhiteSpace(query.Type))
            q = q.Where(c => EF.Functions.Like(c.Type, $"%{EscapeLike(query.Type.Trim())}%", "\\"));

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

    private static string EscapeLike(string s) =>
        s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

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
                dbCard.Name          = card.Name;
                dbCard.SetCode       = card.SetCode;
                dbCard.Rarity        = card.Rarity;
                dbCard.Type          = card.Type;
                dbCard.ManaCost      = card.ManaCost;
                dbCard.Text          = card.Text;
                dbCard.ScryfallId    = card.ScryfallId;
                dbCard.ColorIdentity = card.ColorIdentity;
                dbCard.Artist        = card.Artist;
                dbCard.Power         = card.Power;
                dbCard.Toughness     = card.Toughness;
                dbCard.Loyalty       = card.Loyalty;
                // Frame / finish / collector fields — must be kept in sync with MtgJsonService parsing
                dbCard.FrameEffects     = card.FrameEffects;
                dbCard.BorderColor      = card.BorderColor;
                dbCard.HasNonFoil       = card.HasNonFoil;
                dbCard.HasFoil          = card.HasFoil;
                dbCard.HasEtched        = card.HasEtched;
                dbCard.CollectorNumber  = card.CollectorNumber;
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

    public async Task<Dictionary<string, string>> GetNameToUuidMapAsync(CancellationToken ct = default)
    {
        // Single query returning only the columns we need — replaces the N+1 pattern in GoodGamesScraperService.
        // First UUID wins for duplicate names (same card name printed in multiple sets).
        var pairs = await _db.Cards
            .AsNoTracking()
            .Select(c => new { c.Uuid, c.Name })
            .ToListAsync(ct);

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in pairs)
            map.TryAdd(p.Name.ToLowerInvariant(), p.Uuid);

        return map;
    }

    public async Task<Dictionary<string, List<CardLookupEntry>>> GetCardLookupAsync(CancellationToken ct = default)
    {
        // Single JOIN query — returns every printing with the set name and frame data
        // needed for Good Games product title matching.
        var rows = await _db.Cards
            .AsNoTracking()
            .Include(c => c.Set)
            .Select(c => new
            {
                c.Uuid,
                c.Name,
                c.SetCode,
                SetName         = c.Set.Name,
                c.FrameEffects,
                c.BorderColor,
                c.HasNonFoil,
                c.HasFoil,
                c.HasEtched,
                c.CollectorNumber,
            })
            .ToListAsync(ct);

        var lookup = new Dictionary<string, List<CardLookupEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows)
        {
            var key = r.Name.ToLowerInvariant();
            if (!lookup.TryGetValue(key, out var list))
            {
                list = new List<CardLookupEntry>();
                lookup[key] = list;
            }
            list.Add(new CardLookupEntry(
                r.Uuid, r.SetCode, r.SetName,
                r.FrameEffects, r.BorderColor,
                r.HasNonFoil, r.HasFoil, r.HasEtched,
                r.CollectorNumber));
        }

        return lookup;
    }
}
