using System.Net.Http.Json;
using MTGPriceTracker.Shared.DTOs;

namespace MTGPriceTracker.Client.Services;

/// <summary>
/// Centralised HTTP service for all API calls to the MTGPriceTracker server.
/// </summary>
public class ApiService
{
    private readonly HttpClient _http;

    public ApiService(HttpClient http)
    {
        _http = http;
    }

    // ── Cards ────────────────────────────────────────────────────────────────

    public async Task<PagedResult<CardDto>> GetCardsAsync(CardSearchQuery query)
    {
        var url = BuildCardsUrl(query);
        var result = await _http.GetFromJsonAsync<PagedResult<CardDto>>(url);
        return result ?? new PagedResult<CardDto>();
    }

    public async Task<CardDetailDto?> GetCardAsync(string uuid)
    {
        return await _http.GetFromJsonAsync<CardDetailDto>($"api/cards/{uuid}");
    }

    public async Task<int> GetCardCountAsync()
    {
        return await _http.GetFromJsonAsync<int>("api/cards/count");
    }

    // ── Prices ───────────────────────────────────────────────────────────────

    public async Task<List<VendorPriceHistoryDto>> GetPriceHistoryAsync(string uuid, int days = 90)
    {
        var result = await _http.GetFromJsonAsync<List<VendorPriceHistoryDto>>(
            $"api/prices/{uuid}/history?days={days}");
        return result ?? new List<VendorPriceHistoryDto>();
    }

    // ── Favorites ────────────────────────────────────────────────────────────

    public async Task<List<CardDto>> GetFavoritesAsync()
    {
        var result = await _http.GetFromJsonAsync<List<CardDto>>("api/favorites");
        return result ?? new List<CardDto>();
    }

    public async Task AddFavoriteAsync(string uuid)
    {
        await _http.PostAsync($"api/favorites/{uuid}", null);
    }

    public async Task RemoveFavoriteAsync(string uuid)
    {
        await _http.DeleteAsync($"api/favorites/{uuid}");
    }

    // ── Sync ─────────────────────────────────────────────────────────────────

    public async Task<SyncStatusDto?> GetSyncStatusAsync()
    {
        return await _http.GetFromJsonAsync<SyncStatusDto>("api/sync/status");
    }

    public async Task<SyncTriggerResponse?> TriggerSyncAsync()
    {
        var response = await _http.PostAsync("api/sync/trigger", null);
        return await response.Content.ReadFromJsonAsync<SyncTriggerResponse>();
    }

    public async Task<SyncTriggerResponse?> TriggerCatalogImportAsync()
    {
        var response = await _http.PostAsync("api/sync/import-catalog", null);
        return await response.Content.ReadFromJsonAsync<SyncTriggerResponse>();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string BuildCardsUrl(CardSearchQuery q)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(q.Query)) parts.Add($"query={Uri.EscapeDataString(q.Query)}");
        if (!string.IsNullOrWhiteSpace(q.SetCode)) parts.Add($"setCode={Uri.EscapeDataString(q.SetCode)}");
        if (!string.IsNullOrWhiteSpace(q.Rarity)) parts.Add($"rarity={Uri.EscapeDataString(q.Rarity)}");
        if (!string.IsNullOrWhiteSpace(q.Type)) parts.Add($"type={Uri.EscapeDataString(q.Type)}");
        if (q.FavoritesOnly == true) parts.Add("favoritesOnly=true");
        parts.Add($"page={q.Page}");
        parts.Add($"pageSize={q.PageSize}");
        parts.Add($"sortBy={q.SortBy}");
        parts.Add($"sortDescending={q.SortDescending.ToString().ToLower()}");
        return "api/cards?" + string.Join("&", parts);
    }
}
