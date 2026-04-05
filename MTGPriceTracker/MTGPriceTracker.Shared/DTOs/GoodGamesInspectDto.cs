namespace MTGPriceTracker.Shared.DTOs;

/// <summary>
/// Full details for a single Good Games product, returned by the inspect endpoint.
/// Shows every field the Shopify API returns plus how we matched it to MTGJSON.
/// </summary>
public class GoodGamesProductInspectDto
{
    // ── Raw Shopify fields ───────────────────────────────────────────────────

    public long   ShopifyId   { get; set; }
    public string Title       { get; set; } = string.Empty;
    public string Handle      { get; set; } = string.Empty;
    public string ProductType { get; set; } = string.Empty;
    public string Vendor      { get; set; } = string.Empty;
    public string ProductUrl  { get; set; } = string.Empty;

    /// <summary>All Shopify tags on this product (e.g. set names, foil markers, collector numbers).</summary>
    public List<string> Tags { get; set; } = new();

    public List<GoodGamesVariantInspectDto> Variants { get; set; } = new();

    // ── Parsed / resolved fields (from product title) ────────────────────────

    public string  ParsedCardName   { get; set; } = string.Empty;
    public string? ParsedSetName    { get; set; }
    public string? ParsedArtVariant { get; set; }

    /// <summary>Set code resolved from ParsedSetName via the normalized lookup.</summary>
    public string? ResolvedSetCode { get; set; }

    /// <summary>True if the art variant string is a recognised treatment (Borderless, Showcase, etc.).</summary>
    public bool IsRecognisedArtVariant { get; set; }

    // ── NM prices (from title-based extraction) ───────────────────────────────

    public decimal? NmNormalPrice { get; set; }
    public decimal? NmFoilPrice   { get; set; }

    // ── Match method ──────────────────────────────────────────────────────────

    /// <summary>"SKU" when at least one NM variant resolved via collector number; "Fuzzy" for title-based; "None" if unmatched.</summary>
    public string MatchMethod { get; set; } = "None";

    // ── MTGJSON match result (product-level — for Fuzzy / display purposes) ──

    public bool    Matched      { get; set; }
    public string  MatchReason  { get; set; } = string.Empty;

    // Populated only when Matched = true (from the first/representative match)
    public string? MatchedUuid         { get; set; }
    public string? MatchedSetCode      { get; set; }
    public string? MatchedSetName      { get; set; }
    public string? MatchedFrameEffects { get; set; }
    public string? MatchedBorderColor  { get; set; }
    public bool    MatchedHasNonFoil   { get; set; }
    public bool    MatchedHasFoil      { get; set; }
    public bool    MatchedHasEtched    { get; set; }
    public string? MatchedCollectorNumber { get; set; }

    // What would be written to the DB
    public bool WouldWriteNonFoilPrice { get; set; }
    public bool WouldWriteFoilPrice    { get; set; }

    /// <summary>
    /// SKU-matched snapshots — one per NM variant that resolved to a UUID.
    /// Each shows the exact UUID, finish, and price that would be stored.
    /// Empty when MatchMethod != "SKU".
    /// </summary>
    public List<GoodGamesSkuSnapshotDto> SkuSnapshots { get; set; } = new();
}

/// <summary>One price snapshot that would be written via SKU-based matching.</summary>
public class GoodGamesSkuSnapshotDto
{
    public string  Uuid            { get; set; } = string.Empty;
    public string  CardName        { get; set; } = string.Empty;
    public string  SetCode         { get; set; } = string.Empty;
    public string? CollectorNumber { get; set; }
    public string? FrameEffects    { get; set; }
    public string? BorderColor     { get; set; }
    public string  PriceType       { get; set; } = string.Empty;  // "retail" | "retail_foil" | "retail_etched"
    public decimal Price           { get; set; }
    public string  SkuUsed         { get; set; } = string.Empty;  // the raw SKU string from GG
}

public class GoodGamesVariantInspectDto
{
    public long    Id        { get; set; }
    public string  Title     { get; set; } = string.Empty;
    public string  Price     { get; set; } = "0";
    public bool    Available { get; set; }
    public string? Sku       { get; set; }
    public string? Barcode   { get; set; }
    public string? Option1   { get; set; }
    public string? Option2   { get; set; }

    /// <summary>Parsed condition code from variant title: NM, LP, MP, HP, DMG, or null.</summary>
    public string? ParsedCondition { get; set; }

    /// <summary>True if this variant is foil (from variant title).</summary>
    public bool IsFoil { get; set; }

    // ── SKU parse results ─────────────────────────────────────────────────────

    public string? SkuSetCode         { get; set; }  // e.g. "2X2"
    public string? SkuCollectorNumber { get; set; }  // e.g. "79"
    public string? SkuLanguage        { get; set; }  // e.g. "EN"
    public string? SkuFinish          { get; set; }  // "NF" | "F" | "E"
    public string? SkuPriceType       { get; set; }  // mapped: "retail" | "retail_foil" | "retail_etched"

    /// <summary>UUID resolved from the SKU index for this variant, or null if not found.</summary>
    public string? SkuResolvedUuid    { get; set; }
}

/// <summary>Response wrapper for the inspect endpoint.</summary>
public class GoodGamesInspectResponse
{
    public string SearchTerm   { get; set; } = string.Empty;
    public int    TotalScanned { get; set; }
    public int    TotalMatched { get; set; }
    public int    SkuMatched   { get; set; }
    public int    FuzzyMatched { get; set; }
    public string FetchedAt    { get; set; } = string.Empty;
    public List<GoodGamesProductInspectDto> Products { get; set; } = new();
}
