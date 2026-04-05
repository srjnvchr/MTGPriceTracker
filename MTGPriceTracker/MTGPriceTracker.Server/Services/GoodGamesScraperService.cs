using System.Text.Json;
using System.Text.Json.Serialization;
using MTGPriceTracker.Server.Models;
using MTGPriceTracker.Server.Repositories.Interfaces;
using MTGPriceTracker.Server.Services.Interfaces;
using MTGPriceTracker.Shared.DTOs;

namespace MTGPriceTracker.Server.Services;

/// <summary>
/// Scrapes card prices from Good Games Australia (tcg.goodgames.com.au).
///
/// Strategy: use the Shopify /collections/{handle}/products.json endpoint which returns
/// clean structured JSON including ALL variants (conditions) per product in one pass.
/// This avoids per-card HTML scraping and captures NM, LP, MP, HP, DMG, foil prices.
///
/// Collection handle to verify: tcg.goodgames.com.au/collections/magic-the-gathering/products.json
/// If that 404s, check the URL on their site and update CollectionHandle below.
/// </summary>
public class GoodGamesScraperService : IGoodGamesScraperService
{
    public const string HttpClientName = "goodgames";

    private const string BaseUrl = "https://tcg.goodgames.com.au";

    /// <summary>
    /// Shopify collection handle for MTG singles.
    /// Verify at: https://tcg.goodgames.com.au/collections/magic-the-gathering
    /// </summary>
    private const string CollectionHandle = "magic-the-gathering";

    private const int PageSize = 250; // Shopify max per page
    private const int RequestDelayMs = 500; // polite delay between pages

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ICardRepository _cardRepository;
    private readonly ICardSetRepository _cardSetRepository;
    private readonly IPriceRepository _priceRepository;
    private readonly ILogger<GoodGamesScraperService> _logger;

    public GoodGamesScraperService(
        IHttpClientFactory httpClientFactory,
        ICardRepository cardRepository,
        ICardSetRepository cardSetRepository,
        IPriceRepository priceRepository,
        ILogger<GoodGamesScraperService> logger)
    {
        _httpClient = httpClientFactory.CreateClient(HttpClientName);
        _cardRepository = cardRepository;
        _cardSetRepository = cardSetRepository;
        _priceRepository = priceRepository;
        _logger = logger;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public async Task<Dictionary<string, decimal>> GetCardPricesAsync(
        string cardName, CancellationToken ct = default)
    {
        // NOTE: /search is disallowed in robots.txt — we do NOT use it.
        // Instead we query the collection products.json endpoint filtered by title,
        // which is allowed and returns the same structured variant data.
        //
        // Shopify doesn't support server-side title filtering on the collection JSON
        // endpoint, so we fetch one page and do client-side name matching.
        // For bulk scraping use ScrapeAllPricesAsync instead.

        try
        {
            var url = $"{BaseUrl}/collections/{CollectionHandle}/products.json?limit={PageSize}&page=1";
            var products = await FetchProductsPageAsync(url, ct);
            if (products is null) return new Dictionary<string, decimal>();

            // Find the best matching product by card name (case-insensitive)
            var match = products.FirstOrDefault(p =>
            {
                ParseTitle(p.Title, out var pName, out _, out _);
                return pName.Equals(cardName, StringComparison.OrdinalIgnoreCase);
            });

            if (match is null) return new Dictionary<string, decimal>();

            // For the single-card lookup we still return all conditions (used by the UI
            // to show a live price snapshot regardless of condition).
            return ExtractVariantPrices(match);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetCardPricesAsync failed for {CardName}", cardName);
            return new Dictionary<string, decimal>();
        }
    }

    public async Task<int> ScrapeAllPricesAsync(
        IProgress<string>? progress = null, bool force = false, CancellationToken ct = default)
    {
        var today = DateTime.UtcNow.Date;

        // Skip if already done today — unless the caller explicitly forces a re-run
        if (!force)
        {
            var lastSync = await _priceRepository.GetLastSyncDateAsync("goodgames", ct);
            if (lastSync?.Date == today)
            {
                progress?.Report("Good Games prices already synced today. Use Force Sync to re-run.");
                return 0;
            }
        }

        // Build lookups: name-based (fuzzy fallback) + SKU-based (primary exact path).
        // SKU lookup key: "{SetCode}:{CollectorNumber}" (upper-case, OrdinalIgnoreCase dict).
        progress?.Report("Building card lookup indexes...");
        var cardLookup    = await _cardRepository.GetCardLookupAsync(ct);
        var setCodeLookup = await BuildSetCodeLookupAsync(ct);
        var skuIndex      = BuildSkuIndex(cardLookup);
        progress?.Report($"Indexes: {cardLookup.Count} card names, {skuIndex.Count} collector-number entries.");

        var snapshots = new List<PriceSnapshot>();
        int totalScraped  = 0;
        int skuMatched    = 0;
        int fuzzyMatched  = 0;
        int unmatched     = 0;
        int page          = 1;
        int totalProducts = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var url = $"{BaseUrl}/collections/{CollectionHandle}/products.json" +
                      $"?limit={PageSize}&page={page}";

            List<ShopifyProduct>? products;
            try
            {
                products = await FetchProductsPageAsync(url, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch Good Games collection page {Page}", page);
                progress?.Report($"Error on page {page}: {ex.Message}. Stopping.");
                break;
            }

            if (products is null || products.Count == 0)
                break;

            totalProducts += products.Count;
            progress?.Report(
                $"Page {page}: {products.Count} products " +
                $"(sku:{skuMatched} fuzzy:{fuzzyMatched} unmatched:{unmatched})...");

            foreach (var product in products)
            {
                ct.ThrowIfCancellationRequested();

                // ── Primary path: SKU-based exact match ────────────────────────────
                // Each variant SKU encodes "{SetCode}-{CollectorNumber}-{Lang}-{Finish}-{Ver}".
                // This gives us a deterministic UUID + finish type per variant — no fuzzy needed.
                var skuSnapshots = ExtractNmPricesBySku(product, skuIndex, today);
                if (skuSnapshots.Count > 0)
                {
                    snapshots.AddRange(skuSnapshots);
                    totalScraped += skuSnapshots.Count;
                    skuMatched++;
                    continue;
                }

                // ── Fallback: title-based fuzzy match ──────────────────────────────
                // Used when SKUs are absent, malformed, or the collector number isn't in our DB.
                ParseTitle(product.Title, out var cardName, out var setName, out var artVariant);

                var resolvedSetCode = setName is not null
                    ? ResolveSetCode(setName, setCodeLookup)
                    : null;

                var entry = FindBestMatch(cardName, resolvedSetCode, artVariant, cardLookup);
                if (entry is null)
                {
                    _logger.LogDebug(
                        "No DB match: '{Title}' (name='{N}' set='{S}' → code='{C}' art='{A}')",
                        product.Title, cardName, setName, resolvedSetCode, artVariant);
                    unmatched++;
                    continue;
                }

                var (nmNormal, nmFoil) = ExtractNmPrices(product);

                if (nmNormal.HasValue && entry.HasNonFoil)
                {
                    snapshots.Add(new PriceSnapshot
                    {
                        CardUuid  = entry.Uuid,
                        Vendor    = "goodgames",
                        PriceType = "retail",
                        Condition = "NM",
                        Currency  = "AUD",
                        Price     = nmNormal.Value,
                        Date      = today
                    });
                    totalScraped++;
                }

                if (nmFoil.HasValue && entry.HasFoil)
                {
                    snapshots.Add(new PriceSnapshot
                    {
                        CardUuid  = entry.Uuid,
                        Vendor    = "goodgames",
                        PriceType = "retail_foil",
                        Condition = "NM",
                        Currency  = "AUD",
                        Price     = nmFoil.Value,
                        Date      = today
                    });
                    totalScraped++;
                }

                fuzzyMatched++;
            }

            if (snapshots.Count > 0)
            {
                await _priceRepository.BulkUpsertAsync(snapshots, ct);
                snapshots.Clear();
            }

            progress?.Report(
                $"Page {page} done. " +
                $"sku-matched: {skuMatched}, fuzzy-matched: {fuzzyMatched}, unmatched: {unmatched}.");
            page++;

            await Task.Delay(RequestDelayMs, ct);
        }

        if (snapshots.Count > 0)
            await _priceRepository.BulkUpsertAsync(snapshots, ct);

        progress?.Report(
            $"Good Games complete: {totalScraped} prices written. " +
            $"SKU exact: {skuMatched}, fuzzy fallback: {fuzzyMatched}, unmatched: {unmatched}.");
        _logger.LogInformation(
            "Good Games complete: {Count} prices. SKU:{Sku} Fuzzy:{Fuzzy} Unmatched:{Unmatched}",
            totalScraped, skuMatched, fuzzyMatched, unmatched);

        return totalScraped;
    }

    public async Task<GoodGamesInspectResponse> InspectProductsAsync(
        string searchTerm, CancellationToken ct = default)
    {
        var cardLookup    = await _cardRepository.GetCardLookupAsync(ct);
        var setCodeLookup = await BuildSetCodeLookupAsync(ct);
        var skuIndex      = BuildSkuIndex(cardLookup);

        var response = new GoodGamesInspectResponse
        {
            SearchTerm = searchTerm,
            FetchedAt  = DateTime.UtcNow.ToString("o")
        };

        var lowerSearch = searchTerm.Trim().ToLowerInvariant();
        int page         = 1;
        int totalScanned = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var url = $"{BaseUrl}/collections/{CollectionHandle}/products.json" +
                      $"?limit={PageSize}&page={page}";
            var products = await FetchProductsPageAsync(url, ct);
            if (products is null || products.Count == 0) break;

            totalScanned += products.Count;

            foreach (var product in products)
            {
                ParseTitle(product.Title, out var cardName, out var setName, out var artVariant);
                if (!cardName.ToLowerInvariant().Contains(lowerSearch)) continue;

                var resolvedSetCode    = setName is not null ? ResolveSetCode(setName, setCodeLookup) : null;
                var recognisedArt      = artVariant is not null && IsRecognisedArtVariant(artVariant);
                var (nmNormal, nmFoil) = ExtractNmPrices(product);

                // ── Build per-variant inspection rows (always, for all variants) ──────
                var variantDtos = product.Variants.Select(v =>
                {
                    var condCode = NormaliseCondition(v.Title);
                    var parsed   = ParseSku(v.Sku);
                    string? skuResolvedUuid = null;
                    string? skuPriceType    = null;
                    if (parsed is not null)
                    {
                        var (sc, cn, _, fin) = parsed.Value;
                        if (skuIndex.TryGetValue($"{sc}:{cn}", out var skuEntry))
                            skuResolvedUuid = skuEntry.Uuid;
                        skuPriceType = SkuFinishToPriceType(fin);
                    }
                    return new GoodGamesVariantInspectDto
                    {
                        Id        = v.Id,
                        Title     = v.Title,
                        Price     = v.Price,
                        Available = v.Available,
                        Sku       = v.Sku,
                        Barcode   = v.Barcode,
                        Option1   = v.Option1,
                        Option2   = v.Option2,
                        IsFoil           = condCode?.EndsWith("_FOIL") ?? false,
                        ParsedCondition  = condCode?.Replace("_FOIL", ""),
                        SkuSetCode         = parsed?.SetCode,
                        SkuCollectorNumber = parsed?.CollectorNumber,
                        SkuLanguage        = parsed?.Language,
                        SkuFinish          = parsed?.Finish,
                        SkuPriceType       = skuPriceType,
                        SkuResolvedUuid    = skuResolvedUuid,
                    };
                }).ToList();

                // ── Try SKU-based exact matching (dry-run) ──────────────────────────
                var skuSnapshots = new List<GoodGamesSkuSnapshotDto>();
                foreach (var v in variantDtos)
                {
                    if (v.ParsedCondition is not ("NM")) continue;
                    if (v.SkuResolvedUuid is null || v.SkuPriceType is null) continue;
                    if (!decimal.TryParse(v.Price,
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out var price)
                        || price <= 0) continue;

                    // Look up the entry for finish validation
                    var skuKey = $"{v.SkuSetCode}:{v.SkuCollectorNumber}";
                    if (!skuIndex.TryGetValue(skuKey, out var skuEntry)) continue;
                    if (v.SkuPriceType == "retail"         && !skuEntry.HasNonFoil) continue;
                    if (v.SkuPriceType == "retail_foil"    && !skuEntry.HasFoil)    continue;
                    if (v.SkuPriceType == "retail_etched"  && !skuEntry.HasEtched)  continue;

                    skuSnapshots.Add(new GoodGamesSkuSnapshotDto
                    {
                        Uuid            = skuEntry.Uuid,
                        CardName        = cardName,
                        SetCode         = skuEntry.SetCode,
                        CollectorNumber = skuEntry.CollectorNumber,
                        FrameEffects    = skuEntry.FrameEffects,
                        BorderColor     = skuEntry.BorderColor,
                        PriceType       = v.SkuPriceType,
                        Price           = price,
                        SkuUsed         = v.Sku ?? string.Empty,
                    });
                }

                // ── Fuzzy match (always run, for comparison) ───────────────────────
                CardLookupEntry? fuzzyMatch;
                string matchReason;
                if (!cardLookup.TryGetValue(cardName, out var printings) || printings.Count == 0)
                {
                    fuzzyMatch  = null;
                    matchReason = "Card name not found in database";
                }
                else if (printings.Count == 1)
                {
                    fuzzyMatch  = printings[0];
                    matchReason = $"Only 1 printing in DB → {printings[0].SetCode}";
                }
                else
                {
                    bool hasRec = artVariant is not null && IsRecognisedArtVariant(artVariant);
                    fuzzyMatch  = null;
                    matchReason = string.Empty;

                    if (resolvedSetCode is not null && artVariant is not null)
                    {
                        var best = printings.FirstOrDefault(p =>
                            p.SetCode.Equals(resolvedSetCode, StringComparison.OrdinalIgnoreCase) &&
                            MatchesArtVariant(p, artVariant));
                        if (best is not null)
                        {
                            fuzzyMatch  = best;
                            matchReason = $"Tier 1 (set+art) → [{best.SetCode}] #{best.CollectorNumber} frame={best.FrameEffects} border={best.BorderColor}";
                        }
                        else if (hasRec)
                        {
                            var setHits = printings.Where(p => p.SetCode.Equals(resolvedSetCode, StringComparison.OrdinalIgnoreCase)).ToList();
                            matchReason = $"Tier 1 FAIL — recognised art '{artVariant}' unmatched in {resolvedSetCode} " +
                                          $"({setHits.Count} printing(s): " +
                                          string.Join(", ", setHits.Select(p => $"#{p.CollectorNumber} frame={p.FrameEffects} border={p.BorderColor}")) + ")";
                        }
                        else
                            matchReason = "Tier 1 incomplete — fallthrough";
                    }

                    if (fuzzyMatch is null && matchReason is ("" or "Tier 1 incomplete — fallthrough"))
                    {
                        if (resolvedSetCode is not null)
                        {
                            var setMatches = printings
                                .Where(p => p.SetCode.Equals(resolvedSetCode, StringComparison.OrdinalIgnoreCase))
                                .ToList();
                            if (setMatches.Count == 1)
                            {
                                fuzzyMatch  = setMatches[0];
                                matchReason = $"Tier 2 (set only, 1 match) → [{setMatches[0].SetCode}] #{setMatches[0].CollectorNumber}";
                            }
                            else if (setMatches.Count > 1)
                            {
                                var standard = setMatches.FirstOrDefault(p =>
                                    string.IsNullOrEmpty(p.FrameEffects) && (p.BorderColor is null or "black"));
                                fuzzyMatch  = standard ?? setMatches[0];
                                matchReason = $"Tier 2 (set only, {setMatches.Count} matches) → preferred #{fuzzyMatch.CollectorNumber} frame={fuzzyMatch.FrameEffects} border={fuzzyMatch.BorderColor}";
                            }
                            else
                                matchReason = $"Tier 2 FAIL — no cards named '{cardName}' in set {resolvedSetCode}";
                        }
                        else
                        {
                            var fallback = printings.FirstOrDefault(p =>
                                string.IsNullOrEmpty(p.FrameEffects) && (p.BorderColor is null or "black"));
                            fuzzyMatch  = fallback ?? printings[0];
                            matchReason = $"Tier 3 (name-only, {printings.Count} printings) → [{fuzzyMatch.SetCode}] #{fuzzyMatch.CollectorNumber}";
                        }
                    }
                }

                // ── Decide overall match outcome ────────────────────────────────────
                bool skuSucceeded   = skuSnapshots.Count > 0;
                bool fuzzySucceeded = fuzzyMatch is not null;
                string matchMethod  = skuSucceeded ? "SKU" : fuzzySucceeded ? "Fuzzy" : "None";

                // Representative card entry for the product-level display
                CardLookupEntry? repr = skuSucceeded
                    ? skuIndex.GetValueOrDefault($"{skuSnapshots[0].SetCode}:{skuSnapshots[0].CollectorNumber}")
                    : fuzzyMatch;

                var dto = new GoodGamesProductInspectDto
                {
                    ShopifyId   = product.Id,
                    Title       = product.Title,
                    Handle      = product.Handle,
                    ProductType = product.ProductType,
                    Vendor      = product.Vendor,
                    ProductUrl  = $"{BaseUrl}/products/{product.Handle}",
                    Tags        = product.Tags,
                    Variants    = variantDtos,

                    ParsedCardName         = cardName,
                    ParsedSetName          = setName,
                    ParsedArtVariant       = artVariant,
                    ResolvedSetCode        = resolvedSetCode,
                    IsRecognisedArtVariant = recognisedArt,

                    NmNormalPrice = nmNormal,
                    NmFoilPrice   = nmFoil,

                    MatchMethod = matchMethod,

                    Matched              = skuSucceeded || fuzzySucceeded,
                    MatchReason          = skuSucceeded
                                              ? $"SKU exact match — {skuSnapshots.Count} NM snapshot(s) resolved"
                                              : matchReason,

                    MatchedUuid              = repr?.Uuid,
                    MatchedSetCode           = repr?.SetCode,
                    MatchedSetName           = repr?.SetName,
                    MatchedFrameEffects      = repr?.FrameEffects,
                    MatchedBorderColor       = repr?.BorderColor,
                    MatchedHasNonFoil        = repr?.HasNonFoil  ?? false,
                    MatchedHasFoil           = repr?.HasFoil     ?? false,
                    MatchedHasEtched         = repr?.HasEtched   ?? false,
                    MatchedCollectorNumber   = repr?.CollectorNumber,

                    WouldWriteNonFoilPrice = skuSucceeded
                        ? skuSnapshots.Any(s => s.PriceType == "retail")
                        : fuzzyMatch is not null && nmNormal.HasValue && fuzzyMatch.HasNonFoil,
                    WouldWriteFoilPrice    = skuSucceeded
                        ? skuSnapshots.Any(s => s.PriceType == "retail_foil")
                        : fuzzyMatch is not null && nmFoil.HasValue   && fuzzyMatch.HasFoil,

                    SkuSnapshots = skuSnapshots,
                };

                if (skuSucceeded)      response.SkuMatched++;
                else if (fuzzySucceeded) response.FuzzyMatched++;

                response.Products.Add(dto);
            }

            page++;
            await Task.Delay(RequestDelayMs, ct);
        }

        response.TotalScanned = totalScanned;
        response.TotalMatched = response.Products.Count;
        return response;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<List<ShopifyProduct>?> FetchProductsPageAsync(string url, CancellationToken ct)
    {
        var response = await _httpClient.GetStringAsync(url, ct);
        var page = JsonSerializer.Deserialize<ShopifyProductsResponse>(response, JsonOptions);
        return page?.Products;
    }

    private async Task<ShopifyProduct?> FetchProductJsonAsync(string handle, CancellationToken ct)
    {
        try
        {
            var url = $"{BaseUrl}/products/{handle}.json";
            var json = await _httpClient.GetStringAsync(url, ct);
            var wrapper = JsonSerializer.Deserialize<ShopifyProductWrapper>(json, JsonOptions);
            return wrapper?.Product;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch product JSON for handle: {Handle}", handle);
            return null;
        }
    }

    /// <summary>
    /// Parses a Good Games product title into its three components.
    /// Format: "Card Name (Art Variant) [Set Name]"
    ///
    /// Examples:
    ///   "Lightning Bolt [Magic Player Rewards 2010]"
    ///       → cardName="Lightning Bolt", setName="Magic Player Rewards 2010", artVariant=null
    ///   "Mana Vault (Borderless Alternate Art) [Double Masters 2022]"
    ///       → cardName="Mana Vault", setName="Double Masters 2022", artVariant="Borderless Alternate Art"
    ///   "Ponder (Timeshifted) [Time Spiral Remastered]"
    ///       → cardName="Ponder", setName="Time Spiral Remastered", artVariant="Timeshifted"
    ///   "Jace, the Mind Sculptor"
    ///       → cardName="Jace, the Mind Sculptor", setName=null, artVariant=null
    /// </summary>
    private static void ParseTitle(
        string title,
        out string cardName,
        out string? setName,
        out string? artVariant)
    {
        setName    = null;
        artVariant = null;

        // 1. Extract [Set Name] — use the LAST bracket pair to avoid false matches in card names
        var bracketStart = title.LastIndexOf('[');
        var bracketEnd   = title.LastIndexOf(']');
        string withoutSet;
        if (bracketStart >= 0 && bracketEnd > bracketStart)
        {
            setName    = title[(bracketStart + 1)..bracketEnd].Trim();
            withoutSet = title[..bracketStart].Trim();
        }
        else
        {
            withoutSet = title.Trim();
        }

        // 2. Extract (Art Variant) — use the LAST paren pair so "Jace, Vryn's Prodigy (Showcase)" works
        var parenStart = withoutSet.LastIndexOf('(');
        var parenEnd   = withoutSet.LastIndexOf(')');
        if (parenStart >= 0 && parenEnd > parenStart)
        {
            artVariant = withoutSet[(parenStart + 1)..parenEnd].Trim();
            cardName   = withoutSet[..parenStart].Trim();
        }
        else
        {
            cardName = withoutSet;
        }
    }

    /// <summary>
    /// Finds the best matching card entry from the rich lookup using a strict three-tier priority.
    ///
    /// Key design decisions:
    /// • If the GG title contains a *recognised* art-variant keyword (Borderless, Showcase, etc.)
    ///   but no card in the DB matches it for that set, we return null rather than falling back
    ///   to a wrong printing.  This prevents e.g. "Borderless Alt Art" prices landing on a
    ///   "Serialized Poster" UUID just because it was the only One Ring in the fallback list.
    /// • For tier-2 (set match, no art variant) and tier-3 (name-only), we actively prefer
    ///   standard black-border non-special-frame printings over foil/borderless variants,
    ///   so a plain "Lightning Bolt [LTR]" title doesn't accidentally match a showcase printing.
    /// </summary>
    private static CardLookupEntry? FindBestMatch(
        string cardName,
        string? resolvedSetCode,   // already normalised to a DB set code, or null
        string? artVariant,
        Dictionary<string, List<CardLookupEntry>> cardLookup)
    {
        if (!cardLookup.TryGetValue(cardName, out var printings) || printings.Count == 0)
            return null;

        // Single printing in DB for this name → nothing to disambiguate
        if (printings.Count == 1)
            return printings[0];

        bool hasRecognisedVariant = artVariant is not null && IsRecognisedArtVariant(artVariant);

        // ── Tier 1: set code + art variant ───────────────────────────────────
        if (resolvedSetCode is not null && artVariant is not null)
        {
            var best = printings.FirstOrDefault(p =>
                p.SetCode.Equals(resolvedSetCode, StringComparison.OrdinalIgnoreCase) &&
                MatchesArtVariant(p, artVariant));
            if (best is not null) return best;

            // Recognised variant keyword but nothing matched — bail rather than guess.
            if (hasRecognisedVariant) return null;
        }

        // ── Tier 2: set code only ─────────────────────────────────────────────
        // Reached when artVariant is null or is an unrecognised descriptor (e.g. "Promo Pack").
        if (resolvedSetCode is not null)
        {
            var setMatches = printings
                .Where(p => p.SetCode.Equals(resolvedSetCode, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (setMatches.Count == 1) return setMatches[0];

            if (setMatches.Count > 1)
            {
                // Multiple printings in the same set (regular + showcase + borderless…).
                // Without an art variant, prefer the plain black-border non-special-frame printing.
                var standard = setMatches.FirstOrDefault(p =>
                    string.IsNullOrEmpty(p.FrameEffects) &&
                    (p.BorderColor is null or "black"));
                return standard ?? setMatches[0];
            }

            // Set code resolved but no card with that set code found — skip rather than guess.
            return null;
        }

        // ── Tier 3: name-only fallback (GG title had no set brackets at all) ──
        var fallback = printings.FirstOrDefault(p =>
            string.IsNullOrEmpty(p.FrameEffects) &&
            (p.BorderColor is null or "black"));
        return fallback ?? printings[0];
    }

    /// <summary>
    /// Returns true if the art-variant string contains a keyword we can map to a specific
    /// MTGJSON field.  Unrecognised descriptors (e.g. "Promo Pack", "Bundle") don't count —
    /// we don't want to hard-fail on them, but they don't trigger strict-match mode either.
    /// </summary>
    private static bool IsRecognisedArtVariant(string artVariant)
    {
        var v = artVariant.ToUpperInvariant();
        return v.Contains("BORDERLESS")    ||
               v.Contains("EXTENDED ART") || v.Contains("EXTENDEDART") ||
               v.Contains("SHOWCASE")     ||
               v.Contains("ETCHED")       ||
               v.Contains("FULL ART")     || v.Contains("FULLART")     ||
               v.Contains("RETRO")        || v.Contains("OLD FRAME")   ||
               v.Contains("TIMESHIFTED")  ||
               v.Contains("SERIALIZED");
    }

    /// <summary>
    /// Returns true if the card entry's frame/border data matches the Good Games art variant string.
    ///
    /// Mapping of common GG parentheticals → MTGJSON fields:
    ///   "Borderless"          → borderColor == "borderless"
    ///   "Extended Art"        → frameEffects contains "extendedart"
    ///   "Showcase"            → frameEffects contains "showcase"
    ///   "Etched"              → frameEffects contains "etched"
    ///   "Full Art"            → frameEffects contains "fullart"
    ///   "Retro" / "Old Frame" → frameEffects contains "retro"
    ///   "Timeshifted"         → frameEffects contains "timeshifted"
    ///   "Alternate Art"       → any special frameEffect (broad catch-all)
    ///   Unrecognised          → true (don't penalise unknown variants)
    /// </summary>
    private static bool MatchesArtVariant(CardLookupEntry p, string artVariant)
    {
        var v       = artVariant.ToUpperInvariant();
        var effects = p.FrameEffects?.ToUpperInvariant() ?? string.Empty;
        var border  = p.BorderColor?.ToUpperInvariant() ?? string.Empty;

        if (v.Contains("BORDERLESS"))
            return border == "BORDERLESS";

        if (v.Contains("EXTENDED ART") || v.Contains("EXTENDEDART"))
            return effects.Contains("EXTENDEDART");

        if (v.Contains("SHOWCASE"))
            return effects.Contains("SHOWCASE");

        if (v.Contains("ETCHED"))
            return effects.Contains("ETCHED");

        if (v.Contains("FULL ART") || v.Contains("FULLART"))
            return effects.Contains("FULLART");

        if (v.Contains("RETRO") || v.Contains("OLD FRAME") || v.Contains("OLDFRAME"))
            return effects.Contains("RETRO");

        if (v.Contains("TIMESHIFTED"))
            return effects.Contains("TIMESHIFTED");

        // "Alternate Art", "Alternate", "Alternative" — any special treatment counts
        if (v.Contains("ALTERNATE") || v.Contains("ALTERNATIVE"))
            return !string.IsNullOrEmpty(effects) || border == "BORDERLESS";

        // Unrecognised variant descriptor — don't filter on it; let set-name match decide
        return true;
    }

    /// <summary>
    /// Extracts Near Mint prices only from a Shopify product, returning the non-foil and
    /// foil NM prices separately.  Only available variants with a positive price are included.
    ///
    /// This replaces the old ExtractVariantPrices which returned all conditions — we now
    /// align with MTGJSON's PricePoints model: one entry per finish (normal / foil / etched)
    /// rather than one entry per condition.
    /// </summary>
    private static (decimal? NmNormal, decimal? NmFoil) ExtractNmPrices(ShopifyProduct product)
    {
        decimal? nmNormal = null;
        decimal? nmFoil   = null;

        foreach (var variant in product.Variants)
        {
            if (!decimal.TryParse(variant.Price,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var price))
                continue;
            if (price <= 0) continue;

            var condition = NormaliseCondition(variant.Title);
            if (condition == "NM")      nmNormal ??= price;
            else if (condition == "NM_FOIL") nmFoil ??= price;
        }

        return (nmNormal, nmFoil);
    }

    /// <summary>
    /// Extracts condition→price pairs from a Shopify product's variants.
    ///
    /// Good Games variant titles follow these patterns:
    ///   "NM", "LP", "MP", "HP", "DMG"            → non-foil conditions
    ///   "Foil NM", "Foil LP"                      → foil conditions → stored as "NM_FOIL" etc.
    ///   "Near Mint", "Lightly Played"             → long-form (normalised)
    ///
    /// Only includes available (in-stock) variants with a positive price.
    /// </summary>
    private static Dictionary<string, decimal> ExtractVariantPrices(ShopifyProduct product)
    {
        var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        foreach (var variant in product.Variants)
        {
            if (!decimal.TryParse(variant.Price, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var price))
                continue;
            if (price <= 0) continue;

            var condition = NormaliseCondition(variant.Title);
            if (condition is null) continue;

            // If multiple variants map to the same condition key, keep the lower price
            if (!result.ContainsKey(condition) || price < result[condition])
                result[condition] = price;
        }

        return result;
    }

    private static string? NormaliseCondition(string variantTitle)
    {
        var t = variantTitle.Trim();

        // Good Games uses suffix format: "Near Mint Foil", "Lightly Played Foil" etc.
        bool isFoil = t.EndsWith("Foil", StringComparison.OrdinalIgnoreCase);
        var condPart = isFoil
            ? t[..^"Foil".Length].Trim()  // strip trailing "Foil"
            : t;

        var code = condPart.ToUpperInvariant() switch
        {
            "NM" or "NEAR MINT"         => "NM",
            "LP" or "LIGHTLY PLAYED"    => "LP",
            "MP" or "MODERATELY PLAYED" => "MP",
            "HP" or "HEAVILY PLAYED"    => "HP",
            "DMG" or "DAMAGED"          => "DMG",
            _                           => null
        };

        if (code is null) return null;
        return isFoil ? $"{code}_FOIL" : code;
    }

    // ── SKU-based exact matching ──────────────────────────────────────────────

    /// <summary>
    /// Builds a fast lookup from the card dictionary already in memory.
    /// Key: "{SetCode}:{CollectorNumber}" (OrdinalIgnoreCase).
    /// No extra DB query — runs O(n) over the entries we loaded for fuzzy matching.
    /// Collector numbers are unique per set, so first-entry-wins is correct.
    /// </summary>
    private static Dictionary<string, CardLookupEntry> BuildSkuIndex(
        Dictionary<string, List<CardLookupEntry>> cardLookup)
    {
        var index = new Dictionary<string, CardLookupEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entries in cardLookup.Values)
            foreach (var e in entries)
            {
                if (e.CollectorNumber is null) continue;
                index.TryAdd($"{e.SetCode}:{e.CollectorNumber}", e);
            }
        return index;
    }

    /// <summary>
    /// Parses a GG variant SKU into its structural components.
    ///
    /// Format: "{SetCode}-{CollectorNumber}-{Language}-{Finish}-{Version}"
    ///   "2X2-79-EN-NF-1"   → ("2X2", "79",  "EN", "NF")
    ///   "LTR-380-EN-F-1"   → ("LTR", "380", "EN", "F")
    ///   "LTR-380-EN-E-1"   → ("LTR", "380", "EN", "E")   ← etched (assumed)
    ///
    /// Returns null when the SKU doesn't match the expected ≥4-segment pattern.
    /// </summary>
    private static (string SetCode, string CollectorNumber, string Language, string Finish)? ParseSku(string? sku)
    {
        if (string.IsNullOrWhiteSpace(sku)) return null;
        var parts = sku.Split('-');
        if (parts.Length < 4) return null;
        return (parts[0].ToUpperInvariant(), parts[1], parts[2].ToUpperInvariant(), parts[3].ToUpperInvariant());
    }

    /// <summary>
    /// Maps the GG SKU finish code to the MTGJSON-style PriceType string used in PriceSnapshot.
    ///   NF → "retail"         (non-foil)
    ///   F  → "retail_foil"   (regular foil)
    ///   E  → "retail_etched" (etched foil — assumed, not yet confirmed in the wild)
    /// Returns null for unrecognised codes so we skip rather than guess.
    /// </summary>
    private static string? SkuFinishToPriceType(string finishCode) => finishCode switch
    {
        "NF" => "retail",
        "F"  => "retail_foil",
        "E"  => "retail_etched",
        _    => null
    };

    /// <summary>
    /// Iterates the NM variants of a GG product, parses each variant's SKU, and resolves
    /// the exact UUID from the SKU index.  Returns ready-to-persist PriceSnapshot rows.
    ///
    /// Returns an empty list when no variant has a parseable SKU that resolves to a known card.
    /// In that case the caller should fall back to title-based fuzzy matching.
    /// </summary>
    private static List<PriceSnapshot> ExtractNmPricesBySku(
        ShopifyProduct product,
        Dictionary<string, CardLookupEntry> skuIndex,
        DateTime date)
    {
        var result = new List<PriceSnapshot>();

        foreach (var variant in product.Variants)
        {
            if (!decimal.TryParse(variant.Price,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var price))
                continue;
            if (price <= 0) continue;

            // Only Near Mint variants contribute to price snapshots.
            // We check the variant title as the condition gate, but use the SKU's
            // finish code as the authoritative finish source (more reliable than
            // parsing "Near Mint Foil" vs "Near Mint" from the title).
            // Note: we track prices regardless of stock availability.
            var condCode = NormaliseCondition(variant.Title);
            if (condCode is not ("NM" or "NM_FOIL")) continue;

            var parsed = ParseSku(variant.Sku);
            if (parsed is null) continue;

            var (setCode, collectorNumber, _, finishCode) = parsed.Value;
            if (!skuIndex.TryGetValue($"{setCode}:{collectorNumber}", out var entry)) continue;

            var priceType = SkuFinishToPriceType(finishCode);
            if (priceType is null) continue;

            // Validate the finish is actually available for this printing per MTGJSON.
            if (priceType == "retail"         && !entry.HasNonFoil) continue;
            if (priceType == "retail_foil"    && !entry.HasFoil)    continue;
            if (priceType == "retail_etched"  && !entry.HasEtched)  continue;

            result.Add(new PriceSnapshot
            {
                CardUuid  = entry.Uuid,
                Vendor    = "goodgames",
                PriceType = priceType,
                Condition = "NM",
                Currency  = "AUD",
                Price     = price,
                Date      = date
            });
        }

        return result;
    }

    // ── Set name resolution ───────────────────────────────────────────────────

    /// <summary>
    /// Loads all card sets from the database and builds a lookup:
    ///   normalized set name → set code
    ///   exact set name      → set code  (also added)
    ///   set code            → set code  (identity — lets GG abbreviations like "2XM" resolve)
    ///
    /// Normalization: lowercase, strip punctuation, collapse whitespace, drop leading "the"/"a".
    /// Example: "The Lord of the Rings: Tales of Middle-earth" → "lord of the rings tales of middleearth"
    /// </summary>
    private async Task<Dictionary<string, string>> BuildSetCodeLookupAsync(CancellationToken ct)
    {
        var sets = await _cardSetRepository.GetAllAsync(ct);
        // Use OrdinalIgnoreCase so "2xm" matches "2XM" etc.
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var set in sets)
        {
            lookup.TryAdd(set.Code, set.Code);          // identity (e.g. GG writes "2XM")
            lookup.TryAdd(set.Name, set.Code);          // exact full name
            lookup.TryAdd(NormalizeSetName(set.Name), set.Code); // normalized full name
        }
        return lookup;
    }

    /// <summary>
    /// Normalises a set name for fuzzy comparison.
    /// Removes leading "the "/"a ", strips non-alphanumeric characters, lowercases.
    /// E.g. "The Lord of the Rings: Tales of Middle-earth" → "lord of the rings tales of middleearth"
    /// </summary>
    private static string NormalizeSetName(string name)
    {
        var s = name.Trim().ToLowerInvariant();
        // Strip leading definite/indefinite articles
        if (s.StartsWith("the ")) s = s[4..];
        else if (s.StartsWith("a ")) s = s[2..];
        // Remove all non-alphanumeric (punctuation, colons, dashes, apostrophes, spaces)
        // but keep letters and digits — "middle-earth" → "middleearth"
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var ch in s)
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
        return sb.ToString();
    }

    /// <summary>
    /// Attempts to resolve a GG bracket set name to an MTGJSON set code.
    /// Tries exact lookup first, then normalized lookup.
    /// Returns null if the set is not in the database (very new set, token-only set, etc.).
    /// </summary>
    private static string? ResolveSetCode(string ggSetName, Dictionary<string, string> setCodeLookup)
    {
        // 1. Exact match (handles correct full names and set-code abbreviations like "2XM")
        if (setCodeLookup.TryGetValue(ggSetName, out var code)) return code;

        // 2. Normalized match (handles minor punctuation / article differences)
        var normalized = NormalizeSetName(ggSetName);
        if (setCodeLookup.TryGetValue(normalized, out code)) return code;

        return null;
    }

    // ── Shopify JSON DTOs ─────────────────────────────────────────────────────

    private sealed class ShopifyProductsResponse
    {
        [JsonPropertyName("products")]
        public List<ShopifyProduct> Products { get; set; } = new();
    }

    private sealed class ShopifyProductWrapper
    {
        [JsonPropertyName("product")]
        public ShopifyProduct? Product { get; set; }
    }

    private sealed class ShopifyProduct
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("handle")]
        public string Handle { get; set; } = string.Empty;

        [JsonPropertyName("product_type")]
        public string ProductType { get; set; } = string.Empty;

        [JsonPropertyName("vendor")]
        public string Vendor { get; set; } = string.Empty;

        [JsonPropertyName("tags")]
        public List<string> Tags { get; set; } = new();

        [JsonPropertyName("body_html")]
        public string BodyHtml { get; set; } = string.Empty;

        [JsonPropertyName("variants")]
        public List<ShopifyVariant> Variants { get; set; } = new();
    }

    private sealed class ShopifyVariant
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("price")]
        public string Price { get; set; } = "0";

        [JsonPropertyName("available")]
        public bool Available { get; set; }

        [JsonPropertyName("sku")]
        public string? Sku { get; set; }

        [JsonPropertyName("barcode")]
        public string? Barcode { get; set; }

        [JsonPropertyName("option1")]
        public string? Option1 { get; set; }

        [JsonPropertyName("option2")]
        public string? Option2 { get; set; }
    }
}
