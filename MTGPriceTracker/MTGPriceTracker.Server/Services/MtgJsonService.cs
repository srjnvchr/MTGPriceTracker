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
    private const string AllPricesUrl = "https://mtgjson.com/api/v5/AllPrices.json.gz";
    private const int BatchSize = 500;

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

                var card = new Card
                {
                    Uuid = uuid,
                    Name = cardName,
                    SetCode = setCode,
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
                    ColorIdentity = colorIdentity
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
        progress?.Report("Downloading AllPrices.json.gz...");
        _logger.LogInformation("Starting AllPrices import");

        using var response = await _httpClient.GetAsync(AllPricesUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var gzStream = await response.Content.ReadAsStreamAsync(ct);
        await using var decompressed = new GZipStream(gzStream, CompressionMode.Decompress);

        var today = DateTime.UtcNow.Date;
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

            // MTGJSON structure: { "paper": { "vendor": { "retail": { "date": price } } } }
            if (!cardPrices.TryGetProperty("paper", out var paper)) continue;

            foreach (var vendorProp in paper.EnumerateObject())
            {
                var vendor = vendorProp.Name;
                var vendorData = vendorProp.Value;
                var currency = GetVendorCurrency(vendor);

                foreach (var priceTypeProp in vendorData.EnumerateObject())
                {
                    var priceType = priceTypeProp.Name; // retail or buylist
                    var pricesByDate = priceTypeProp.Value;

                    if (pricesByDate.ValueKind != JsonValueKind.Object) continue;

                    // Get only the most recent date's price
                    string? latestDateStr = null;
                    decimal latestPrice = 0;

                    foreach (var dateProp in pricesByDate.EnumerateObject())
                    {
                        if (latestDateStr == null || string.Compare(dateProp.Name, latestDateStr, StringComparison.Ordinal) > 0)
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

                    snapshotBuffer.Add(new PriceSnapshot
                    {
                        CardUuid = uuid,
                        Vendor = vendor,
                        PriceType = priceType,
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

        // Flush remaining
        if (snapshotBuffer.Count > 0)
        {
            await _priceRepository.BulkUpsertAsync(snapshotBuffer, ct);
            totalImported += snapshotBuffer.Count;
        }

        progress?.Report($"AllPrices import complete: {totalImported} price snapshots.");
        _logger.LogInformation("AllPrices import complete: {Count} snapshots", totalImported);

        return totalImported;
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
