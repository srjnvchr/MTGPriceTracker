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
    │              IMtgJsonService, IGoodGamesScraperService)
    └── Repositories (ICardRepository, IPriceRepository, IFavoriteRepository)
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
│   │   ├── IPriceRepository.cs
│   │   └── IFavoriteRepository.cs
│   ├── CardRepository.cs
│   ├── PriceRepository.cs
│   └── FavoriteRepository.cs
├── Services/
│   ├── Interfaces/
│   │   ├── ICardService.cs
│   │   ├── IPriceService.cs
│   │   ├── IFavoritesService.cs
│   │   ├── IMtgJsonService.cs
│   │   └── IGoodGamesScraperService.cs
│   ├── CardService.cs          ← Search, browse, card lookups
│   ├── PriceService.cs         ← Price history queries and aggregation
│   ├── FavoritesService.cs     ← Manage user favorites
│   ├── MtgJsonService.cs       ← Downloads + imports MTGJSON daily builds
│   └── GoodGamesScraperService.cs ← Scrapes goodgames.com.au
├── BackgroundServices/
│   └── PriceSyncBackgroundService.cs ← Daily sync job at midnight AEST
├── Controllers/
│   ├── CardsController.cs      ← GET /api/cards, GET /api/cards/{uuid}
│   ├── PricesController.cs     ← GET /api/prices/{uuid}/history
│   ├── FavoritesController.cs  ← GET/POST/DELETE /api/favorites
│   └── SyncController.cs       ← POST /api/sync/trigger
├── Program.cs
└── appsettings.json
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
│   └── Favorites.razor         ← Favorited cards grid
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
| PriceType | TEXT | retail/buylist |
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
   - Run once initially, then only for new sets

2. **AllPrices.json.gz** (~15MB compressed) — Price data
   - Contains all vendors, price types, and dates
   - Only imports the **latest date's prices** to avoid massive data duplication
   - Full historic retention from the first import date onward

---

## Good Games Scraper

- Target: `https://tcg.goodgames.com.au`
- Strategy: Paginate the Shopify `/collections/magic-the-gathering/products.json` endpoint (250 products/page) — no HTML parsing
- Each product returns all variants (NM, LP, MP, HP, DMG, foil) with prices as clean JSON
- Matching: product title parsed to extract card name (text before `[set name]`), then matched to DB by name (case-insensitive, first UUID wins per name)
- Rate limiting: 500 ms between pages
- Scheduled: Part of daily sync job at 1:00 AM AEST
- Stored as `goodgames` vendor with AUD currency and per-condition `Condition` column
- **Collection handle to verify**: if scraper returns 0 results, visit tcg.goodgames.com.au and check the URL path for their MTG singles collection, then update `CollectionHandle` in `GoodGamesScraperService.cs`

---

## Background Sync Schedule

| Job | Time (AEST) | Description |
|---|---|---|
| MTGJSON Prices | 1:00 AM | Download + import AllPrices.json |
| Good Games | 2:00 AM | Scrape prices for all tracked cards |

---

## Future Roadmap

- [ ] Rule-based notification system (e.g. "alert when TCGPlayer > Good Games AUD")
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
