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
| Currency | TEXT | USD/EUR/AUD |
| Price | REAL | Price value |
| Date | TEXT | ISO date (YYYY-MM-DD) |

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
| Vendor | Currency | Method |
|---|---|---|
| goodgames | AUD | Web scraper (goodgames.com.au) |

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

- Target: `https://www.goodgames.com.au`
- Strategy: Search by card name, parse product listings
- Rate limiting: 1 request/second to be polite
- Scheduled: Daily at 2 AM AEST (after MTGJSON sync)
- Stored as `goodgames` vendor in PriceSnapshots with AUD currency

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
- [ ] Card condition tracking (NM, LP, MP, HP)
- [ ] Foil price tracking
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
