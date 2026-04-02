using HtmlAgilityPack;
using MTGPriceTracker.Server.Models;
using MTGPriceTracker.Server.Repositories.Interfaces;
using MTGPriceTracker.Server.Services.Interfaces;

namespace MTGPriceTracker.Server.Services;

/// <summary>
/// Scrapes card prices from Good Games Australia (goodgames.com.au).
/// Good Games uses a Shopify-based store, so we search via their collection endpoint.
/// NOTE: URL structure may change — verify against the live site periodically.
/// </summary>
public class GoodGamesScraperService : IGoodGamesScraperService
{
    public const string HttpClientName = "goodgames";

    private const string BaseUrl = "https://tcg.goodgames.com.au";
    private const string SearchUrl = "https://tcg.goodgames.com.au/search?q={0}";
    private const int RequestDelayMs = 1000; // 1 second between requests — be polite

    private readonly HttpClient _httpClient;
    private readonly ICardRepository _cardRepository;
    private readonly IPriceRepository _priceRepository;
    private readonly ILogger<GoodGamesScraperService> _logger;

    public GoodGamesScraperService(
        IHttpClientFactory httpClientFactory,
        ICardRepository cardRepository,
        IPriceRepository priceRepository,
        ILogger<GoodGamesScraperService> logger)
    {
        _httpClient = httpClientFactory.CreateClient(HttpClientName);
        _cardRepository = cardRepository;
        _priceRepository = priceRepository;
        _logger = logger;
    }

    public async Task<decimal?> GetCardPriceAsync(string cardName, CancellationToken ct = default)
    {
        try
        {
            var url = string.Format(SearchUrl, Uri.EscapeDataString(cardName));
            _logger.LogDebug("Scraping Good Games for: {CardName} at {Url}", cardName, url);

            var html = await _httpClient.GetStringAsync(url, ct);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // tcg.goodgames.com.au Shopify theme HTML (verified):
            //
            // Normal price:
            //   <div class="product-prices no-sale">
            //     <span class="price.no_sale">$31.80 AUD</span>
            //   </div>
            //
            // Sale price:
            //   <div class="product-prices">
            //     <span class="discounted_price">$24.00 AUD</span>
            //     <span class="price">$31.80</span>   ← hidden compare-at
            //   </div>

            // 1. Prefer the visible no-sale span (most cards)
            var priceNode = doc.DocumentNode.SelectSingleNode(
                "//div[contains(@class,'product-prices') and contains(@class,'no-sale')]/span");

            // 2. Fall back to discounted_price span (sale items)
            priceNode ??= doc.DocumentNode.SelectSingleNode(
                "//span[contains(@class,'discounted_price')]");

            // 3. Last resort: any span inside a product-prices div
            priceNode ??= doc.DocumentNode.SelectSingleNode(
                "//div[contains(@class,'product-prices')]/span");

            if (priceNode is null) return null;

            var price = ParseAudPrice(priceNode.InnerText.Trim());
            return price.HasValue && price.Value > 0 ? price : null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to reach Good Games for card: {CardName}", cardName);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scraping Good Games for card: {CardName}", cardName);
            return null;
        }
    }

    public async Task<int> ScrapeAllPricesAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var uuids = (await _cardRepository.GetAllUuidsAsync(ct)).ToList();
        var today = DateTime.UtcNow.Date;
        var snapshots = new List<PriceSnapshot>();
        int scraped = 0;

        // For efficiency, we only scrape cards that haven't had Good Games prices today
        var lastSync = await _priceRepository.GetLastSyncDateAsync("goodgames", ct);
        if (lastSync?.Date == today)
        {
            progress?.Report("Good Games prices already synced today. Skipping.");
            return 0;
        }

        progress?.Report($"Scraping Good Games prices for {uuids.Count} cards...");

        // For performance, we work with unique card names (many UUID variants share a name)
        // We'll get all unique card names first
        var allCards = new List<(string Uuid, string Name)>();

        // We process in small batches — don't load all 30k+ cards into memory at once
        const int chunkSize = 100;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < uuids.Count; i += chunkSize)
        {
            ct.ThrowIfCancellationRequested();
            var chunk = uuids.Skip(i).Take(chunkSize).ToList();

            foreach (var uuid in chunk)
            {
                var card = await _cardRepository.GetByUuidAsync(uuid, ct);
                if (card is null) continue;

                // Only scrape one UUID per unique card name
                if (!seen.Add(card.Name)) continue;

                allCards.Add((card.Uuid, card.Name));
            }
        }

        progress?.Report($"Scraping {allCards.Count} unique card names from Good Games...");

        foreach (var (uuid, name) in allCards)
        {
            ct.ThrowIfCancellationRequested();

            var price = await GetCardPriceAsync(name, ct);
            if (price.HasValue)
            {
                snapshots.Add(new PriceSnapshot
                {
                    CardUuid = uuid,
                    Vendor = "goodgames",
                    PriceType = "retail",
                    Currency = "AUD",
                    Price = price.Value,
                    Date = today
                });
                scraped++;
            }

            // Rate limit — be polite
            await Task.Delay(RequestDelayMs, ct);

            if (scraped % 50 == 0)
            {
                progress?.Report($"Scraped {scraped} Good Games prices...");
                if (snapshots.Count >= 100)
                {
                    await _priceRepository.BulkUpsertAsync(snapshots, ct);
                    snapshots.Clear();
                }
            }
        }

        if (snapshots.Count > 0)
            await _priceRepository.BulkUpsertAsync(snapshots, ct);

        progress?.Report($"Good Games scraping complete: {scraped} prices collected.");
        _logger.LogInformation("Good Games scraping complete: {Count} prices", scraped);

        return scraped;
    }

    private static decimal? ParseAudPrice(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        // Remove currency symbols, whitespace, and non-numeric chars except . and ,
        var cleaned = text.Replace("$", "").Replace("AUD", "").Replace(",", "").Trim();

        if (decimal.TryParse(cleaned, System.Globalization.NumberStyles.Currency,
            System.Globalization.CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }

        return null;
    }
}
