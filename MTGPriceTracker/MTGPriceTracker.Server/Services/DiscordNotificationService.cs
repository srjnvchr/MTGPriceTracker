using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MTGPriceTracker.Server.Data;
using MTGPriceTracker.Server.Services.Interfaces;
using MTGPriceTracker.Shared.DTOs;

namespace MTGPriceTracker.Server.Services;

public class DiscordNotificationService : IDiscordNotificationService
{
    public const string HttpClientName = "Discord";

    private const decimal MinPriceChangePercent = 10m;
    private const decimal MinCurrentPrice = 3.0m;
    private const int LookbackDays = 7;
    private const int EmbedsPerMessage = 10;

    // Thread IDs keyed by UTC date — persists for the app's lifetime.
    // If the app restarts on the same day a new thread is opened, which is fine.
    private static readonly ConcurrentDictionary<DateOnly, string> s_threadCache = new();

    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<DiscordNotificationService> _logger;

    public DiscordNotificationService(
        AppDbContext db,
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<DiscordNotificationService> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    // ── Query ────────────────────────────────────────────────────────────────

    public async Task<List<PriceAlertDto>> FindAlertsAsync(CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-LookbackDays);

        // 1. Pull all TCGPlayer + Card Kingdom retail prices (foil and non-foil) for the window.
        var intlSnapshots = await _db.PriceSnapshots
            .AsNoTracking()
            .Where(p => (p.Vendor == "tcgplayer" || p.Vendor == "cardkingdom")
                     && (p.PriceType == "retail" || p.PriceType == "retail_foil")
                     && p.Condition == null
                     && p.Date >= cutoff)
            .ToListAsync(ct);

        // 2. Pull all Good Games prices (full history in window for change detection,
        //    plus any older snapshot to establish a baseline).
        var ggSnapshots = await _db.PriceSnapshots
            .AsNoTracking()
            .Where(p => p.Vendor == "goodgames"
                     && p.PriceType == "retail"
                     && p.Condition == "NM")
            .GroupBy(p => p.CardUuid)
            .Select(g => new
            {
                CardUuid   = g.Key,
                // Latest GG price regardless of date
                LatestPrice = g.OrderByDescending(p => p.Date).First().Price,
                LatestDate  = g.OrderByDescending(p => p.Date).First().Date,
                // Oldest GG price within the lookback window (for change detection)
                OldestInWindow = g
                    .Where(p => p.Date >= cutoff)
                    .OrderBy(p => p.Date)
                    .Select(p => (decimal?)p.Price)
                    .FirstOrDefault()
            })
            .ToDictionaryAsync(x => x.CardUuid, ct);

        if (ggSnapshots.Count == 0)
            return new List<PriceAlertDto>();

        // 3. For each card+vendor+priceType series, find the best jump.
        //    Key: (CardUuid, Vendor, PriceType)
        var jumpsBySeries = intlSnapshots
            .GroupBy(p => (p.CardUuid, p.Vendor, p.PriceType))
            .Select(g =>
            {
                var sorted = g.OrderBy(p => p.Date).ToList();
                if (sorted.Count < 2) return null;

                var oldest = sorted.First();
                var latest = sorted.Last();
                if (oldest.Price <= 0m) return null;

                var pct = (latest.Price - oldest.Price) / oldest.Price * 100m;
                return new
                {
                    g.Key.CardUuid,
                    g.Key.Vendor,
                    g.Key.PriceType,
                    OldPrice      = oldest.Price,
                    NewPrice      = latest.Price,
                    ChangePercent = pct,
                    OldDate       = oldest.Date,
                    NewDate       = latest.Date
                };
            })
            .Where(x => x != null
                     && x.NewPrice >= MinCurrentPrice
                     && x.ChangePercent >= MinPriceChangePercent)
            .ToList();

        if (jumpsBySeries.Count == 0)
            return new List<PriceAlertDto>();

        // 4. Filter: card must have Good Games pricing AND GG must not have jumped ≥10% itself.
        var qualifying = jumpsBySeries
            .Where(x =>
            {
                if (!ggSnapshots.TryGetValue(x!.CardUuid, out var gg)) return false; // no GG price → skip

                // If GG has both an old and new price in the window, check its change.
                if (gg.OldestInWindow.HasValue && gg.OldestInWindow.Value > 0m)
                {
                    var ggChangePct = (gg.LatestPrice - gg.OldestInWindow.Value)
                                      / gg.OldestInWindow.Value * 100m;
                    if (ggChangePct >= MinPriceChangePercent) return false; // GG already repriced → skip
                }

                return true;
            })
            .ToList();

        if (qualifying.Count == 0)
            return new List<PriceAlertDto>();

        // 5. Per card: keep only the single best (highest %) jump to avoid duplicate notifications.
        var bestPerCard = qualifying
            .GroupBy(x => x!.CardUuid)
            .Select(g => g.OrderByDescending(x => x!.ChangePercent).First())
            .OrderByDescending(x => x!.ChangePercent)
            .ToList();

        // 6. Fetch card metadata in one query.
        var uuids = bestPerCard.Select(x => x!.CardUuid).ToList();
        var cardInfo = await _db.Cards
            .AsNoTracking()
            .Where(c => uuids.Contains(c.Uuid))
            .Select(c => new
            {
                c.Uuid,
                c.Name,
                c.SetCode,
                c.CollectorNumber,
                c.BorderColor,
                c.FrameEffects,
                c.HasFoil,
                c.HasNonFoil
            })
            .ToDictionaryAsync(c => c.Uuid, ct);

        return bestPerCard
            .Where(x => cardInfo.ContainsKey(x!.CardUuid))
            .Select(x =>
            {
                var card = cardInfo[x!.CardUuid];
                var gg   = ggSnapshots[x.CardUuid];
                return new PriceAlertDto
                {
                    CardUuid        = x.CardUuid,
                    CardName        = card.Name,
                    SetCode         = card.SetCode,
                    CollectorNumber = card.CollectorNumber,
                    BorderColor     = card.BorderColor,
                    FrameEffects    = card.FrameEffects,
                    HasFoil         = card.HasFoil,
                    HasNonFoil      = card.HasNonFoil,
                    Vendor          = x.Vendor == "tcgplayer" ? "TCGPlayer" : "Card Kingdom",
                    IsFoil          = x.PriceType == "retail_foil",
                    OldPrice        = x.OldPrice,
                    NewPrice        = x.NewPrice,
                    ChangePercent   = Math.Round(x.ChangePercent, 1),
                    Currency        = "USD",
                    OldDate         = x.OldDate,
                    NewDate         = x.NewDate,
                    GoodGamesPrice  = gg.LatestPrice
                };
            })
            .ToList();
    }

    // ── Discord webhook ──────────────────────────────────────────────────────

    /// <summary>
    /// Posts alerts to a Discord Forum-channel thread for today's date.
    /// Green (moderate) alerts are posted first, yellow (significant) second,
    /// and red (high-value) last — so red cards sit at the bottom of the thread
    /// and are immediately visible when anyone opens it.
    ///
    /// The webhook URL in appsettings must point to a Discord Forum channel
    /// so that each call without a thread_id creates a new named thread.
    /// </summary>
    public async Task PostAlertsAsync(List<PriceAlertDto> alerts)
    {
        var webhookUrl = _config["Discord:WebhookUrl"];
        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            _logger.LogWarning("Discord webhook URL is not configured — skipping post.");
            return;
        }

        if (alerts.Count == 0)
            return;

        // ── 1. Classify by severity ──────────────────────────────────────────
        var yellowThreshold = decimal.TryParse(_config["Discord:YellowThresholdPct"], out var y) ? y : 25m;
        var redThreshold    = decimal.TryParse(_config["Discord:RedThresholdPct"],    out var r) ? r : 50m;

        var green  = alerts.Where(a => a.ChangePercent <  yellowThreshold)
                           .OrderByDescending(a => a.ChangePercent).ToList();
        var yellow = alerts.Where(a => a.ChangePercent >= yellowThreshold && a.ChangePercent < redThreshold)
                           .OrderByDescending(a => a.ChangePercent).ToList();
        var red    = alerts.Where(a => a.ChangePercent >= redThreshold)
                           .OrderByDescending(a => a.ChangePercent).ToList();

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            var today  = DateOnly.FromDateTime(DateTime.UtcNow);

            // ── 2. Create or reuse today's Forum thread ──────────────────────
            if (!s_threadCache.TryGetValue(today, out var threadId))
            {
                var threadName = $"📊 {DateTime.UtcNow:dd MMM yyyy} — MTG Buying Opportunities";
                var summary    = $"🟢 **{green.Count}** moderate" +
                                 $"  🟡 **{yellow.Count}** significant" +
                                 $"  🔴 **{red.Count}** high-value";

                // POST with ?wait=true so Discord returns the full message object
                // containing channel_id = the newly created thread's ID.
                var openPayload = new
                {
                    username    = "MTG Price Tracker",
                    thread_name = threadName,
                    embeds      = new[]
                    {
                        new
                        {
                            description = summary,
                            color       = 0x5865F2   // Discord blurple — neutral header
                        }
                    }
                };

                var responseBody = await SendAndReadAsync(client, $"{webhookUrl}?wait=true", openPayload);
                using var doc    = JsonDocument.Parse(responseBody);
                threadId         = doc.RootElement.GetProperty("channel_id").GetString()!;
                s_threadCache[today] = threadId;

                _logger.LogInformation("Created Discord thread {ThreadId} for {Date}", threadId, today);
            }

            var threadUrl = $"{webhookUrl}?thread_id={threadId}";

            // ── 3. Send groups in order: green → yellow → red ────────────────
            //    Red is sent last so it sits at the bottom of the thread —
            //    the most accessible position when the thread is opened.
            await SendEmbedGroupAsync(client, threadUrl, green,  label: "🟢 Moderate spikes",     color: 0x2DC770);
            await SendEmbedGroupAsync(client, threadUrl, yellow, label: "🟡 Significant spikes",   color: 0xFAAD14);
            await SendEmbedGroupAsync(client, threadUrl, red,    label: "🔴 High-value proposals", color: 0xE53935);

            _logger.LogInformation(
                "Discord alerts posted — 🟢 {G}  🟡 {Y}  🔴 {R}  thread={T}",
                green.Count, yellow.Count, red.Count, threadId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post Discord price alerts.");
        }
    }

    // Sends a labelled group header then batches of up to 10 embeds into the thread.
    private async Task SendEmbedGroupAsync(
        HttpClient client, string threadUrl,
        List<PriceAlertDto> group, string label, int color)
    {
        if (group.Count == 0) return;

        // Section header
        await SendMessageAsync(client, threadUrl, new
        {
            username = "MTG Price Tracker",
            content  = $"**{label}** — {group.Count} card{(group.Count == 1 ? "" : "s")}"
        });

        for (int i = 0; i < group.Count; i += EmbedsPerMessage)
        {
            var batch  = group.Skip(i).Take(EmbedsPerMessage).ToList();
            var embeds = batch.Select(alert => new
            {
                title  = FormatTitle(alert),
                color,
                fields = BuildFields(alert),
                footer = new { text = $"{alert.Vendor} · {alert.OldDate:dd MMM} → {alert.NewDate:dd MMM}" }
            }).ToList();

            await SendMessageAsync(client, threadUrl, new { username = "MTG Price Tracker", embeds });

            if (i + EmbedsPerMessage < group.Count)
                await Task.Delay(1000); // stay within Discord rate limits
        }
    }

    // ── Formatting helpers ───────────────────────────────────────────────────

    private static string FormatTitle(PriceAlertDto alert)
    {
        var finish = alert.IsFoil ? "✨ Foil" : "Non-Foil";
        var num    = alert.CollectorNumber != null ? $" #{alert.CollectorNumber}" : string.Empty;
        return $"{alert.CardName} [{alert.SetCode}{num}] · {finish}";
    }

    private static object[] BuildFields(PriceAlertDto alert)
    {
        var border  = FormatBorder(alert.BorderColor, alert.FrameEffects);
        var finishes = FormatFinishes(alert.HasFoil, alert.HasNonFoil);
        var ggPrice = alert.GoodGamesPrice.HasValue
            ? $"AU${alert.GoodGamesPrice.Value:F2}"
            : "Not stocked";

        return new object[]
        {
            new { name = "Was",            value = $"${alert.OldPrice:F2} USD",         inline = true },
            new { name = "Now",            value = $"${alert.NewPrice:F2} USD",         inline = true },
            new { name = "Jump",           value = $"+{alert.ChangePercent:F1}%",       inline = true },
            new { name = "Good Games",     value = ggPrice,                             inline = true },
            new { name = "Border/Frame",   value = border,                              inline = true },
            new { name = "Finishes",       value = finishes,                            inline = true }
        };
    }

    private static string FormatBorder(string? borderColor, string? frameEffects)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(borderColor) && borderColor != "black")
            parts.Add(System.Globalization.CultureInfo.InvariantCulture.TextInfo
                          .ToTitleCase(borderColor));

        if (!string.IsNullOrWhiteSpace(frameEffects))
        {
            foreach (var fx in frameEffects.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var label = fx switch
                {
                    "showcase"    => "Showcase",
                    "extendedart" => "Extended Art",
                    "borderless"  => "Borderless",
                    "etched"      => "Etched",
                    "fullart"     => "Full Art",
                    _             => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(fx)
                };
                parts.Add(label);
            }
        }

        return parts.Count > 0 ? string.Join(", ", parts) : "Standard";
    }

    private static string FormatFinishes(bool hasFoil, bool hasNonFoil)
    {
        if (hasFoil && hasNonFoil) return "Foil + Non-Foil";
        if (hasFoil) return "Foil only";
        return "Non-Foil only";
    }

    private static async Task SendMessageAsync(HttpClient client, string webhookUrl, object payload)
    {
        await SendAndReadAsync(client, webhookUrl, payload);
    }

    private static async Task<string> SendAndReadAsync(HttpClient client, string webhookUrl, object payload)
    {
        var json     = JsonSerializer.Serialize(payload);
        var content  = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await client.PostAsync(webhookUrl, content);
        var body     = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Discord webhook returned {(int)response.StatusCode}: {body}");

        return body;
    }
}
