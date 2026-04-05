using System.IO.Compression;
using System.Text.Json;
using MTGPriceTracker.Server.Models;
using MTGPriceTracker.Server.Repositories.Interfaces;
using MTGPriceTracker.Server.Services.Interfaces;

namespace MTGPriceTracker.Server.Services;

/// <summary>
/// Imports card catalog and price data from the MTGJSON daily build files.
/// Files are downloaded from https://mtgjson.com/api/v5/
/// </summary>
public class MtgJsonService : IMtgJsonService
{
    public const string HttpClientName = "mtgjson";

    private const string AllPrintingsUrl = "https://mtgjson.com/api/v5/AllPrintings.json.gz";
    // AllPricesToday contains only the current day's prices (same structure as AllPrices but
    // with a single date entry per vendor). It is ~30 MB decompressed vs 500 MB+ for the full
    // AllPrices file, which causes an int-overflow in JsonDocument's internal buffer growth.
    private const string AllPricesUrl = "https://mtgjson.com/api/v5/AllPricesToday.json.gz";
    private const string AllPricesHistoryUrl = "https://mtgjson.com/api/v5/AllPrices.json.gz";
    // How many price snapshots to accumulate before flushing to the DB.
    // 50 000 means ~340 BulkUpsert calls for a 17M-row history import instead of 34 000.
    // BulkUpsertAsync itself commits every 50 000 rows internally, so this aligns perfectly.
    private const int BatchSize = 50_000;

    private readonly ICardRepository _cardRepository;
    private readonly ICardSetRepository _cardSetRepository;
    private readonly IPriceRepository _priceRepository;
    private readonly HttpClient _httpClient;
    private readonly ILogger<MtgJsonService> _logger;

    public MtgJsonService(
        ICardRepository cardRepository,
        ICardSetRepository cardSetRepository,
        IPriceRepository priceRepository,
        IHttpClientFactory httpClientFactory,
        ILogger<MtgJsonService> logger)
    {
        _cardRepository = cardRepository;
        _cardSetRepository = cardSetRepository;
        _priceRepository = priceRepository;
        _httpClient = httpClientFactory.CreateClient(HttpClientName);
        _logger = logger;
    }

    public async Task<(int setsImported, int cardsImported)> ImportAllPrintingsAsync(
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        progress?.Report("Downloading AllPrintings.json.gz...");
        _logger.LogInformation("Starting AllPrintings import");

        using var response = await _httpClient.GetAsync(AllPrintingsUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var gzStream = await response.Content.ReadAsStreamAsync(ct);
        await using var decompressed = new GZipStream(gzStream, CompressionMode.Decompress);

        int setsImported = 0;
        int cardsImported = 0;
        var setBuffer = new List<CardSet>(10);
        var cardBuffer = new List<Card>(BatchSize);

        using var jsonDoc = await JsonDocument.ParseAsync(decompressed, cancellationToken: ct);

        if (!jsonDoc.RootElement.TryGetProperty("data", out var data))
        {
            _logger.LogWarning("AllPrintings.json has unexpected structure — no 'data' key");
            return (0, 0);
        }

        foreach (var setProp in data.EnumerateObject())
        {
            ct.ThrowIfCancellationRequested();

            var setCode = setProp.Name;
            var setElement = setProp.Value;

            // Parse set metadata
            var setName = setElement.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? setCode : setCode;
            var releaseDate = setElement.TryGetProperty("releaseDate", out var rdProp) ? rdProp.GetString() : null;
            var setType = setElement.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;

            var cardSet = new CardSet
            {
                Code = setCode,
                Name = setName,
                ReleaseDate = releaseDate,
                SetType = setType
            };
            setBuffer.Add(cardSet);

            // Parse cards in this set
            if (!setElement.TryGetProperty("cards", out var cardsArray))
                continue;

            foreach (var cardElement in cardsArray.EnumerateArray())
            {
                var uuid = cardElement.TryGetProperty("uuid", out var uuidProp) ? uuidProp.GetString() : null;
                var cardName = cardElement.TryGetProperty("name", out var cnProp) ? cnProp.GetString() : null;

                if (string.IsNullOrEmpty(uuid) || string.IsNullOrEmpty(cardName))
                    continue;

                // Extract color identity
                string? colorIdentity = null;
                if (cardElement.TryGetProperty("colorIdentity", out var ciProp) && ciProp.ValueKind == JsonValueKind.Array)
                    colorIdentity = string.Join("", ciProp.EnumerateArray().Select(c => c.GetString()));

                // Extract Scryfall ID
                string? scryfallId = null;
                if (cardElement.TryGetProperty("identifiers", out var identifiers))
                {
                    if (identifiers.TryGetProperty("scryfallId", out var sfId))
                        scryfallId = sfId.GetString();
                    else if (identifiers.TryGetProperty("scryfallIllustrationId", out var sfIllustId))
                        scryfallId = sfIllustId.GetString();
                }

                // Extract frame effects — used to match Good Games product variants
                // e.g. "showcase", "extendedart", "etched".
                // Also append "fullart" if isFullArt=true (basics + special non-basics).
                // Stored as a comma-separated string for easy contains-checks.
                string? frameEffects = null;
                {
                    var effects = new List<string>();
                    if (cardElement.TryGetProperty("frameEffects", out var feProp)
                        && feProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var fe in feProp.EnumerateArray())
                        {
                            var v = fe.GetString();
                            if (!string.IsNullOrEmpty(v)) effects.Add(v.ToLowerInvariant());
                        }
                    }
                    if (cardElement.TryGetProperty("isFullArt", out var faP) && faP.GetBoolean()
                        && !effects.Contains("fullart"))
                        effects.Add("fullart");
                    if (effects.Count > 0) frameEffects = string.Join(",", effects);
                }

                // borderColor: "borderless" | "black" | "white" | "gold" | "silver"
                string? borderColor = cardElement.TryGetProperty("borderColor", out var bcProp)
                    ? bcProp.GetString()?.ToLowerInvariant()
                    : null;

                // finishes[]: ["nonfoil"], ["foil"], ["nonfoil","foil"], ["etched"], etc.
                // Drives which price variant rows get created per vendor (especially Good Games).
                bool hasNonFoil = false, hasFoil = false, hasEtched = false;
                if (cardElement.TryGetProperty("finishes", out var finishesProp)
                    && finishesProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var finishItem in finishesProp.EnumerateArray())
                    {
                        var f = finishItem.GetString()?.ToLowerInvariant();
                        if (f == "nonfoil")     hasNonFoil = true;
                        else if (f == "foil")   hasFoil    = true;
                        else if (f == "etched") hasEtched  = true;
                    }
                }
                // If MTGJSON didn't include finishes (older sets), assume nonfoil exists.
                if (!hasNonFoil && !hasFoil && !hasEtched) hasNonFoil = true;

                // number: collector number within the set (e.g. "79", "351", "M380", "★1").
                // Used with setCode for exact-match lookups against Good Games SKU values
                // (format: "{SetCode}-{CollectorNumber}-{Language}-{Finish}-{Version}").
                string? collectorNumber = cardElement.TryGetProperty("number", out var numProp)
                    ? numProp.GetString()
                    : null;

                var card = new Card
                {
                    Uuid = uuid,
                    Name = cardName,
                    SetCode = setCode,
                    CollectorNumber = collectorNumber,
                    Rarity = cardElement.TryGetProperty("rarity", out var rProp) ? rProp.GetString() ?? "" : "",
                    Type = cardElement.TryGetProperty("type", out var tProp) ? tProp.GetString() ?? "" : "",
                    ManaCost = cardElement.TryGetProperty("manaCost", out var mcProp) ? mcProp.GetString() : null,
                    Text = cardElement.TryGetProperty("text", out var txProp) ? txProp.GetString() : null,
                    FlavorText = cardElement.TryGetProperty("flavorText", out var ftProp) ? ftProp.GetString() : null,
                    Power = cardElement.TryGetProperty("power", out var pwProp) ? pwProp.GetString() : null,
                    Toughness = cardElement.TryGetProperty("toughness", out var tgProp) ? tgProp.GetString() : null,
                    Loyalty = cardElement.TryGetProperty("loyalty", out var lyProp) ? lyProp.GetString() : null,
                    Artist = cardElement.TryGetProperty("artist", out var artProp) ? artProp.GetString() : null,
                    ScryfallId = scryfallId,
                    ColorIdentity = colorIdentity,
                    FrameEffects = frameEffects,
                    BorderColor = borderColor,
                    HasNonFoil = hasNonFoil,
                    HasFoil = hasFoil,
                    HasEtched = hasEtched,
                };

                cardBuffer.Add(card);

                if (cardBuffer.Count >= BatchSize)
                {
                    await FlushCardBufferAsync(setBuffer, cardBuffer, ct);
                    cardsImported += cardBuffer.Count;
                    setsImported += setBuffer.Count;
                    setBuffer.Clear();
                    cardBuffer.Clear();
                    progress?.Report($"Imported {cardsImported} cards so far...");
                }
            }
        }

        // Flush remaining
        if (setBuffer.Count > 0 || cardBuffer.Count > 0)
        {
            await FlushCardBufferAsync(setBuffer, cardBuffer, ct);
            cardsImported += cardBuffer.Count;
            setsImported += setBuffer.Count;
        }

        progress?.Report($"AllPrintings import complete: {setsImported} sets, {cardsImported} cards.");
        _logger.LogInformation("AllPrintings import complete: {Sets} sets, {Cards} cards", setsImported, cardsImported);

        return (setsImported, cardsImported);
    }

    public async Task<int> ImportAllPricesAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        // Skip if we already imported MTGJSON prices today (tcgplayer is a reliable sentinel vendor)
        var today = DateTime.UtcNow.Date;
        var lastSync = await _priceRepository.GetLastSyncDateAsync("tcgplayer", ct);
        if (lastSync?.Date == today)
        {
            progress?.Report("MTGJSON prices already imported today — skipping download.");
            _logger.LogInformation("MTGJSON prices already imported today, skipping");
            return 0;
        }

        progress?.Report("Downloading AllPricesToday.json.gz...");
        _logger.LogInformation("Starting AllPrices import (today-only snapshot)");

        using var response = await _httpClient.GetAsync(AllPricesUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var gzStream = await response.Content.ReadAsStreamAsync(ct);
        await using var decompressed = new GZipStream(gzStream, CompressionMode.Decompress);

        int totalImported = 0;
        var snapshotBuffer = new List<PriceSnapshot>(BatchSize);

        // Get all known card UUIDs to avoid importing prices for unknown cards
        var knownUuids = (await _cardRepository.GetAllUuidsAsync(ct)).ToHashSet();

        if (knownUuids.Count == 0)
        {
            progress?.Report("No cards in database. Run AllPrintings import first.");
            return 0;
        }

        using var jsonDoc = await JsonDocument.ParseAsync(decompressed, cancellationToken: ct);

        if (!jsonDoc.RootElement.TryGetProperty("data", out var data))
        {
            _logger.LogWarning("AllPrices.json has unexpected structure");
            return 0;
        }

        foreach (var cardProp in data.EnumerateObject())
        {
            ct.ThrowIfCancellationRequested();

            var uuid = cardProp.Name;
            if (!knownUuids.Contains(uuid)) continue;

            var cardPrices = cardProp.Value;

            // MTGJSON structure:
            //   { "paper": { "vendor": { "retail": { "normal": { "date": price }, "foil": { "date": price } } } } }
            // The extra "finish" level (normal/foil) sits between priceType and the date map.
            if (!cardPrices.TryGetProperty("paper", out var paper)) continue;

            foreach (var vendorProp in paper.EnumerateObject())
            {
                var vendor = vendorProp.Name;
                var vendorData = vendorProp.Value;
                var currency = GetVendorCurrency(vendor);

                foreach (var priceTypeProp in vendorData.EnumerateObject())
                {
                    var priceType = priceTypeProp.Name; // "retail" or "buylist"
                    var finishMap = priceTypeProp.Value; // { "normal": { date: price }, "foil": { date: price } }

                    if (finishMap.ValueKind != JsonValueKind.Object) continue;

                    foreach (var finishProp in finishMap.EnumerateObject())
                    {
                        var finish = finishProp.Name;       // "normal" or "foil"
                        var pricesByDate = finishProp.Value; // { "2024-01-15": 1.23 }

                        if (pricesByDate.ValueKind != JsonValueKind.Object) continue;

                        // Get only the most recent date's price
                        string? latestDateStr = null;
                        decimal latestPrice = 0;

                        foreach (var dateProp in pricesByDate.EnumerateObject())
                        {
                            if (latestDateStr == null ||
                                string.Compare(dateProp.Name, latestDateStr, StringComparison.Ordinal) > 0)
                            {
                                if (dateProp.Value.TryGetDecimal(out var price))
                                {
                                    latestDateStr = dateProp.Name;
                                    latestPrice = price;
                                }
                            }
                        }

                        if (latestDateStr == null) continue;
                        if (!DateTime.TryParse(latestDateStr, out var priceDate)) continue;

                        // Encode finish into PriceType: "retail" (normal), "retail_foil", "buylist", "buylist_foil"
                        var fullPriceType = finish == "normal" ? priceType : $"{priceType}_{finish}";

                        snapshotBuffer.Add(new PriceSnapshot
                        {
                            CardUuid = uuid,
                            Vendor = vendor,
                            PriceType = fullPriceType,
                            Currency = currency,
                            Price = latestPrice,
                            Date = priceDate
                        });

                        if (snapshotBuffer.Count >= BatchSize)
                        {
                            await _priceRepository.BulkUpsertAsync(snapshotBuffer, ct);
                            totalImported += snapshotBuffer.Count;
                            snapshotBuffer.Clear();
                            progress?.Report($"Imported {totalImported} price snapshots...");
                        }
                    }
                }
            }
        }

        // Flush remaining
        if (snapshotBuffer.Count > 0)
        {
            await _priceRepository.BulkUpsertAsync(snapshotBuffer, ct);
            totalImported += snapshotBuffer.Count;
        }

        progress?.Report($"AllPricesToday import complete: {totalImported} price snapshots.");
        _logger.LogInformation("AllPricesToday import complete: {Count} snapshots", totalImported);

        return totalImported;
    }

    public async Task<int> ImportAllPricesHistoryAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        // Skip if history already exists (more than 1 distinct date means we've already backfilled)
        var distinctDates = await _priceRepository.GetDistinctPriceDatesCountAsync(ct);
        if (distinctDates > 1)
        {
            progress?.Report($"Price history already present ({distinctDates} days). Skipping backfill.");
            _logger.LogInformation("History backfill skipped — {Dates} distinct dates already in DB", distinctDates);
            return 0;
        }

        progress?.Report("Starting 90-day price history backfill from AllPrices.json.gz...");
        _logger.LogInformation("Starting AllPrices history backfill");

        // Download to a temp .gz file, then decompress to a temp .json file.
        // AllPrices.json decompresses to ~500 MB–1 GB — too large for JsonDocument to handle
        // from an unseekable GZipStream (causes int overflow in its internal buffer doubling).
        // Reading from a seekable FileStream lets JsonDocument pre-allocate the right amount.
        var tempGzPath   = Path.GetTempFileName();
        var tempJsonPath = tempGzPath + ".json";

        try
        {
            // Step 1 — download compressed file
            progress?.Report("Downloading AllPrices.json.gz (~150 MB)...");
            using var response = await _httpClient.GetAsync(AllPricesHistoryUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            await using (var netStream  = await response.Content.ReadAsStreamAsync(ct))
            await using (var fileStream = File.Create(tempGzPath))
            {
                await netStream.CopyToAsync(fileStream, ct);
            }

            progress?.Report("Decompressing AllPrices.json.gz...");

            // Step 2 — decompress to a plain JSON temp file (seekable)
            await using (var gzFile   = File.OpenRead(tempGzPath))
            await using (var gz       = new GZipStream(gzFile, CompressionMode.Decompress))
            await using (var jsonFile = File.Create(tempJsonPath))
            {
                await gz.CopyToAsync(jsonFile, ct);
            }

            progress?.Report("Parsing price history — this may take several minutes...");

            // Step 3 — parse from seekable FileStream (no buffer overflow)
            await using var jsonRead = File.OpenRead(tempJsonPath);
            using var jsonDoc = await JsonDocument.ParseAsync(jsonRead, cancellationToken: ct);

            if (!jsonDoc.RootElement.TryGetProperty("data", out var data))
            {
                _logger.LogWarning("AllPrices.json has unexpected structure — no 'data' key");
                return 0;
            }

            var knownUuids = (await _cardRepository.GetAllUuidsAsync(ct)).ToHashSet();
            int totalImported = 0;
            var snapshotBuffer = new List<PriceSnapshot>(BatchSize);

            // Drop indexes before bulk loading — SQLite must maintain every index B-tree on
            // every INSERT. Dropping first and rebuilding after is ~2x faster for large loads.
            progress?.Report("Dropping indexes for bulk load...");
            await _priceRepository.DropPriceSnapshotIndexesAsync(ct);

            try
            {
                foreach (var cardProp in data.EnumerateObject())
                {
                    ct.ThrowIfCancellationRequested();

                    var uuid = cardProp.Name;
                    if (!knownUuids.Contains(uuid)) continue;

                    if (!cardProp.Value.TryGetProperty("paper", out var paper)) continue;

                    foreach (var vendorProp in paper.EnumerateObject())
                    {
                        var vendor   = vendorProp.Name;
                        var currency = GetVendorCurrency(vendor);

                        foreach (var priceTypeProp in vendorProp.Value.EnumerateObject())
                        {
                            // Only import retail — skip buylist to keep DB size manageable
                            if (!priceTypeProp.Name.Equals("retail", StringComparison.OrdinalIgnoreCase)) continue;

                            foreach (var finishProp in priceTypeProp.Value.EnumerateObject())
                            {
                                var finish       = finishProp.Name;
                                var pricesByDate = finishProp.Value;

                                if (pricesByDate.ValueKind != JsonValueKind.Object) continue;

                                var fullPriceType = finish == "normal" ? "retail" : "retail_foil";

                                foreach (var dateProp in pricesByDate.EnumerateObject())
                                {
                                    if (!DateTime.TryParse(dateProp.Name, out var priceDate)) continue;
                                    if (!dateProp.Value.TryGetDecimal(out var price))          continue;

                                    snapshotBuffer.Add(new PriceSnapshot
                                    {
                                        CardUuid  = uuid,
                                        Vendor    = vendor,
                                        PriceType = fullPriceType,
                                        Currency  = currency,
                                        Price     = price,
                                        Date      = priceDate
                                    });

                                    if (snapshotBuffer.Count >= BatchSize)
                                    {
                                        await _priceRepository.BulkInsertAsync(snapshotBuffer, ct);
                                        totalImported += snapshotBuffer.Count;
                                        snapshotBuffer.Clear();
                                        progress?.Report($"Inserted {totalImported:N0} snapshots...");
                                    }
                                }
                            }
                        }
                    }
                }

                // Flush remainder
                if (snapshotBuffer.Count > 0)
                {
                    await _priceRepository.BulkInsertAsync(snapshotBuffer, ct);
                    totalImported += snapshotBuffer.Count;
                }
            }
            finally
            {
                // Always rebuild indexes — even if the import was cancelled or threw.
                // A partial import with rebuilt indexes is far better than no indexes at all.
                progress?.Report($"Rebuilding indexes on {totalImported:N0} rows (may take a minute)...");
                await _priceRepository.RebuildPriceSnapshotIndexesAsync(ct);
            }

            progress?.Report($"History backfill complete: {totalImported:N0} price snapshots across ~90 days.");
            _logger.LogInformation("History backfill complete: {Count} snapshots", totalImported);
            return totalImported;
        }
        finally
        {
            // Always clean up temp files
            try { if (File.Exists(tempGzPath))   File.Delete(tempGzPath);   } catch { /* ignore */ }
            try { if (File.Exists(tempJsonPath))  File.Delete(tempJsonPath); } catch { /* ignore */ }
        }
    }

    private async Task FlushCardBufferAsync(List<CardSet> sets, List<Card> cards, CancellationToken ct)
    {
        if (sets.Count > 0)
            await _cardSetRepository.BulkUpsertAsync(sets, ct);
        if (cards.Count > 0)
            await _cardRepository.BulkUpsertAsync(cards, ct);
    }

    private static string GetVendorCurrency(string vendor) => vendor switch
    {
        "cardmarket" => "EUR",
        "goodgames" => "AUD",
        _ => "USD"
    };
}
