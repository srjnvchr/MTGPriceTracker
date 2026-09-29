# MTG Price Tracker — Architecture & Project Map

## Overview

A Blazor WebAssembly hosted application for tracking Magic: The Gathering card prices across multiple vendors including TCGPlayer, Card Kingdom, CardMarket, and local Australian resellers like Good Games. Price history is sourced from MTGJSON daily builds and web scraping, stored in SQLite for historic trend analysis.

---

## Tech Stack

| Layer | Technology |
|---|---|
| Frontend | Blazor WebAssembly + MudBlazor v7 |
| Charts | Blazor-ApexCharts |
| Backend | ASP.NET Core 8 Web API |
| Database | SQLite via Entity Framework Core 8 |
| ORM | Entity Framework Core (Code-First) |
| Scraping | HtmlAgilityPack |
| Price Data | MTGJSON API (daily builds) |
| Background Jobs | IHostedService |
| Notifications | Discord webhook (forum-channel threads) |

---

## Solution Structure

```
MTGPriceTracker.sln
├── MTGPriceTracker.Server/      ← ASP.NET Core Web API (hosts WASM client)
├── MTGPriceTracker.Client/      ← Blazor WebAssembly frontend
└── MTGPriceTracker.Shared/      ← Shared DTOs between client and server
```

---

## Backend Architecture (Server)

### Design Principles (SOLID)
- **S** — Each service/repository has a single focused responsibility
- **O** — Services are extended via interfaces without modifying existing code
- **L** — Implementations are substitutable through their interfaces
- **I** — Interfaces are focused (ICardRepository vs IPriceRepository vs IFavoriteRepository)
- **D** — All dependencies injected via constructor; controllers depend on abstractions

### Layer Diagram
```
Controllers
    └── Services (ICardService, IPriceService, IFavoritesService,
    │              IMtgJsonService, IGoodGamesScraperService,
    │              IDiscordNotificationService)
    └── Repositories (ICardRepository, ICardSetRepository,
    │                 IPriceRepository, IFavoriteRepository)
            └── AppDbContext (EF Core → SQLite)
```

### Key Server Files

```
MTGPriceTracker.Server/
├── Models/                     ← EF Core domain entities
│   ├── Card.cs
│   ├── CardSet.cs
│   ├── PriceSnapshot.cs
│   └── UserFavorite.cs
├── Data/
│   └── AppDbContext.cs         ← EF Core DbContext + seeding
├── Repositories/
│   ├── Interfaces/
│   │   ├── ICardRepository.cs
│   │   ├── ICardSetRepository.cs
│   │   ├── IPriceRepository.cs
│   │   └── IFavoriteRepository.cs
│   ├── CardRepository.cs
│   ├── CardSetRepository.cs
│   ├── PriceRepository.cs
│   └── FavoriteRepository.cs
├── Services/
│   ├── Interfaces/
│   │   ├── ICardService.cs
│   │   ├── IPriceService.cs
│   │   ├── IFavoritesService.cs
│   │   ├── IMtgJsonService.cs
│   │   ├── IGoodGamesScraperService.cs
│   │   └── IDiscordNotificationService.cs
│   ├── CardService.cs          ← Search, browse, card lookups
│   ├── PriceService.cs         ← Price history queries and aggregation
│   ├── FavoritesService.cs     ← Manage user favorites
│   ├── MtgJsonService.cs       ← Downloads + imports MTGJSON catalog, daily prices, history backfill
│   ├── GoodGamesScraperService.cs ← Scrapes tcg.goodgames.com.au (Shopify JSON)
│   └── DiscordNotificationService.cs ← Finds price-spike alerts, posts to Discord
├── BackgroundServices/
│   ├── PriceSyncBackgroundService.cs ← Daily sync job at 1:00 AM AEST
│   └── SyncState.cs            ← Singleton sync status shared with SyncController
├── Controllers/
│   ├── CardsController.cs      ← GET /api/cards, /api/cards/{uuid}, /api/cards/count
│   ├── PricesController.cs     ← GET /api/prices/{uuid}/history, /latest
│   ├── FavoritesController.cs  ← GET /api/favorites, GET {uuid}/status, POST/DELETE {uuid}
│   ├── SyncController.cs       ← Sync triggers, status, imports, GG inspector (see below)
│   └── NotificationsController.cs ← POST /api/notifications/discord/price-alerts
├── Program.cs                  ← DI, named HttpClients, WAL pragmas, column migrations
└── appsettings.json

Legacy placeholder files (CardPriceController, CardPriceService, WebScraperService,
WeatherForecastController) are empty/template leftovers kept to avoid git conflicts.

### Sync API (`/api/sync`)
| Endpoint | Purpose |
|---|---|
| `GET status` | Sync state + total cards / price snapshots |
| `POST trigger` | Full sync (MTGJSON prices → Good Games) |
| `POST trigger-goodgames` | Good Games only, bypasses "already synced today" guard |
| `POST import-catalog` | AllPrintings import (first-time setup) |
| `POST import-history` | One-time 90-day price backfill |
| `DELETE prices/{vendor}` | Delete all snapshots for a vendor |
| `GET inspect-goodgames?search=` | Read-only dry run of GG matching for debugging |
```

---

## Frontend Architecture (Client)

### Pages
| Route | Page | Description |
|---|---|---|
| `/` | Home.razor | Search bar + quick stats |
| `/cards` | Cards.razor | Browseable card list with lazy load |
| `/cards/{uuid}` | CardDetail.razor | Card details + price history charts |
| `/favorites` | Favorites.razor | User's favorited cards |
| `/sync` | Sync.razor | Trigger imports/syncs, view progress, send Discord alerts |
| `/debug/goodgames` | GoodGamesDebug.razor | Good Games product matching inspector |

### Services
- `ApiService.cs` — Wrapper for all HTTP calls to the server API

### Key Client Files
```
MTGPriceTracker.Client/
├── Layout/
│   ├── MainLayout.razor        ← Dark MudBlazor theme, nav drawer
│   └── NavMenu.razor           ← Sidebar navigation
├── Pages/
│   ├── Home.razor              ← Search, quick access
│   ├── Cards.razor             ← Card list with virtualization
│   ├── CardDetail.razor        ← Price charts per vendor
│   ├── Favorites.razor         ← Favorited cards grid
│   ├── Sync.razor              ← Management: imports, sync, Discord alerts
│   └── GoodGamesDebug.razor    ← GG inspector
├── Components/
│   ├── MtgCardTile.razor       ← Full card image tile
│   ├── CardGridItem.razor      ← Grid cell wrapper
│   └── Header.razor
├── Services/
│   └── ApiService.cs           ← All API calls
├── _Imports.razor
├── App.razor
└── Program.cs
```

---

## Database Schema

### Cards
| Column | Type | Notes |
|---|---|---|
| Uuid | TEXT PK | MTGJSON UUID |
| Name | TEXT | Card name |
| SetCode | TEXT FK | → CardSets.Code |
| Rarity | TEXT | common/uncommon/rare/mythic |
| Type | TEXT | Creature, Instant, etc. |
| ManaCost | TEXT | e.g. {2}{U}{U} |
| Text | TEXT | Oracle text |
| ScryfallId | TEXT | For image URLs |
| Artist | TEXT | |
| FlavorText, Power, Toughness, Loyalty, ColorIdentity | TEXT | Nullable card attributes |
| FrameEffects | TEXT | Comma-separated (showcase, extendedart, etched, fullart...); used for GG matching |
| BorderColor | TEXT | "borderless" / "black" / "white" |
| HasNonFoil / HasFoil / HasEtched | INTEGER | Available finishes (from MTGJSON `finishes`) |
| CollectorNumber | TEXT | MTGJSON `number`; enables exact SKU matching against Good Games |

Columns added after first release are applied at startup by `ALTER TABLE` in `Program.cs` (EnsureCreated does not alter existing tables).

### CardSets
| Column | Type | Notes |
|---|---|---|
| Code | TEXT PK | e.g. "MH3" |
| Name | TEXT | Full set name |
| ReleaseDate | TEXT | ISO date |

### PriceSnapshots
| Column | Type | Notes |
|---|---|---|
| Id | INTEGER PK | Auto-increment |
| CardUuid | TEXT FK | → Cards.Uuid |
| Vendor | TEXT | tcgplayer/cardkingdom/cardmarket/goodgames |
| PriceType | TEXT | retail/buylist/retail_foil (and other foil variants) |
| Condition | TEXT | NM/LP/MP/HP/DMG/NM_FOIL/LP_FOIL/… Null for MTGJSON market prices |
| Currency | TEXT | USD/EUR/AUD |
| Price | REAL | Price value |
| Date | TEXT | ISO date (YYYY-MM-DD) |

Unique index: `(CardUuid, Vendor, PriceType, Condition, Date)` — SQLite treats each NULL as distinct so MTGJSON rows (null condition) don't collide with each other.

### UserFavorites
| Column | Type | Notes |
|---|---|---|
| Id | INTEGER PK | |
| CardUuid | TEXT FK | → Cards.Uuid |
| AddedAt | TEXT | ISO timestamp |

---

## Vendor & Pricing

### MTGJSON Vendors (auto-imported daily)
| Vendor | Currency | Price Types |
|---|---|---|
| tcgplayer | USD | retail, buylist |
| cardkingdom | USD | retail, buylist |
| cardmarket | EUR | retail |

### Local Vendors (scraped)
| Vendor | Currency | Method | Conditions |
|---|---|---|---|
| goodgames | AUD | Shopify JSON API (tcg.goodgames.com.au) | NM, LP, MP, HP, DMG + foil variants |

### Currency Conversion
- Stored in native currency
- AUD/USD/EUR conversion applied at display time
- Exchange rates fetched from open exchange rates API (future)

---

## MTGJSON Import Strategy

The daily import downloads two files from `https://mtgjson.com/api/v5/`:

1. **AllPrintings.json.gz** (~50MB compressed) — Card catalog
   - Streamed and parsed with `System.Text.Json`
   - Upserts cards and sets in batches of 500
   - Run once initially (`POST /api/sync/import-catalog`), then only for new sets

2. **AllPricesToday.json** — Nightly price data
   - Imported by the daily sync; one new date's snapshot per run
   - Full historic retention from the first import date onward

3. **AllPrices.json** (~150MB) — One-time 90-day history backfill
   - `POST /api/sync/import-history`; downloaded to a temp file to avoid in-memory buffer overflow
   - Skips automatically if more than one distinct date already exists

---

## Good Games Scraper

- Target: `https://tcg.goodgames.com.au`
- Strategy: Paginate the Shopify `/collections/magic-the-gathering/products.json` endpoint (250 products/page) — no HTML parsing
- Each product returns all variants (NM, LP, MP, HP, DMG, foil) with prices as clean JSON
- Matching (primary): variant SKU `{SetCode}-{CollectorNumber}-{Lang}-{Finish}-{Ver}` resolved against `Cards.SetCode` + `CollectorNumber`, respecting `HasNonFoil`/`HasFoil`
- Matching (fallback): product title parsed to card name (text before `[set name]`) and matched by name (case-insensitive, first UUID wins)
- Rate limiting: 500 ms between pages
- Scheduled: Runs after the MTGJSON import in the nightly sync job; `force` flag bypasses the once-per-day guard for manual runs
- Stored as `goodgames` vendor with AUD currency and per-condition `Condition` column
- Collection handle is `magic-the-gathering`; if the scraper returns 0 results, check the URL path on tcg.goodgames.com.au and update `CollectionHandle` in `GoodGamesScraperService.cs`
- Debug with the `/debug/goodgames` page or `GET /api/sync/inspect-goodgames`

---

## Background Sync Schedule

A single job (`PriceSyncBackgroundService`) runs at **1:00 AM AEST** and executes sequentially:

1. Import MTGJSON prices
2. Scrape Good Games prices

Progress and last result are exposed via `SyncState` / `GET /api/sync/status`. Discord alerts are **not** part of the nightly job yet; they are triggered manually from the Sync page.

---

## Discord Price Alerts

- `DiscordNotificationService.FindAlertsAsync` looks for cards whose TCGPlayer or Card Kingdom retail price (foil and non-foil) rose >= 10% over the last 7 days and is >= $3, **and** whose Good Games NM price has not itself risen >= 10% (a buying opportunity before local repricing). Cards with no Good Games price are skipped; one alert (best jump) per card.
- `PostAlertsAsync` posts to a Discord **forum** webhook: one thread per UTC day (cached in memory), alerts sent as embed batches of 10, grouped green/moderate (< 25%), yellow/significant (>= 25%), red/high-value (>= 50%). Thresholds come from `Discord:YellowThresholdPct` / `RedThresholdPct`.
- The controller queries within the request, then posts in a background task so client timeouts can't cancel the Discord calls.
- Webhook URL is read from `Discord:WebhookUrl`; keep it in user secrets / environment variables rather than `appsettings.json`.

---

## Future Roadmap

- [x] Discord price alerts (manual trigger) — implemented
- [ ] Rule-based notification system (e.g. "alert when TCGPlayer > Good Games AUD")
- [ ] Run Discord alerts automatically after the nightly sync
- [ ] Currency conversion with live rates
- [ ] User accounts / multi-user favorites
- [x] Card condition tracking (NM, LP, MP, HP, DMG) — implemented for Good Games
- [x] Foil price tracking — implemented (NM_FOIL, LP_FOIL, …)
- [ ] More Australian resellers (Auggies, Magic Madhouse AU)
- [ ] Price alerts via email/push

---

## Key Decisions Log

| Date | Decision | Reason |
|---|---|---|
| 2026-04-02 | SQLite over PostgreSQL | Simpler local setup; can migrate to Postgres later with same EF Core code |
| 2026-04-02 | Blazor WASM hosted over Blazor Server | Better scalability; API-first lets us add mobile later |
| 2026-04-02 | Repository pattern | Testability and clean separation of data access from business logic |
| 2026-04-02 | MTGJSON as primary data source | Official MTG data with daily price updates across all major vendors |
| 2026-04-02 | Blazor-ApexCharts for price charts | Rich interactive time-series charts vs MudBlazor's basic MudChart |
| 2026-04-02 | Store prices in native currency | Avoids stale conversion data; convert at display time |
| 2026-04-03 | Condition column on PriceSnapshot | Good Games sells NM/LP/MP/HP/DMG separately; MTGJSON is condition-agnostic (null) |
| 2026-04-03 | Shopify JSON API over HTML scraping for Good Games | Structured JSON from /collections/{handle}/products.json is more reliable, captures all variants in one pass, and doesn't break when the theme changes |
| 2026-04-03 | IServiceScopeFactory in SyncController | IServiceProvider injected into controllers is request-scoped and disposed on response; IServiceScopeFactory is singleton and safe for fire-and-forget background tasks |
| 2026-04-05 | Split catalog import, daily price import and history backfill into separate triggers | Decouples heavy one-time downloads from the nightly job; sync status shared through a `SyncState` singleton |
| 2026-04-05 | SKU-based exact matching for Good Games (name match as fallback) | SKUs encode set + collector number + finish, avoiding wrong-printing matches; needed `CollectorNumber`, `FrameEffects`, `BorderColor`, `Has*` finish columns |
| 2026-04-05 | Startup `ALTER TABLE` column migrations + SQLite WAL | EnsureCreated doesn't alter existing tables; WAL + `synchronous=NORMAL` speeds bulk imports |
| 2026-04-05 | Show full Scryfall card image as the card tile | Better browsing than cropped art |
| 2026-04-07 | Discord alerts via webhook; DB query in request, posting in background `Task.Run` | Posting many embeds exceeded client timeouts and crashed the server |
| 2026-04-07 | Alert only on international price jumps where Good Games hasn't repriced | Highlights actual buying opportunities rather than every spike |
| 2026-04-22 | One Discord forum thread per day, embeds grouped by severity | Keeps the channel tidy; high-value alerts sit at the bottom of the thread |
| 2026-05-01 | Fix card grid layout so all cards display | Cards page grid was dropping items |
