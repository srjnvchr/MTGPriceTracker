using Microsoft.EntityFrameworkCore;
using MTGPriceTracker.Server.BackgroundServices;
using MTGPriceTracker.Server.Data;
using MTGPriceTracker.Server.Repositories;
using MTGPriceTracker.Server.Repositories.Interfaces;
using MTGPriceTracker.Server.Services;
using MTGPriceTracker.Server.Services.Interfaces;

var builder = WebApplication.CreateBuilder(args);

// ── Database ──────────────────────────────────────────────────────────────────
var dbPath = builder.Configuration["Database:Path"] ?? "mtg-prices.db";
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<SqliteTuningInterceptor>();
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
    options.UseSqlite($"Data Source={dbPath}")
           .AddInterceptors(sp.GetRequiredService<SqliteTuningInterceptor>()));

// ── Repositories ─────────────────────────────────────────────────────────────
builder.Services.AddScoped<ICardRepository, CardRepository>();
builder.Services.AddScoped<ICardSetRepository, CardSetRepository>();
builder.Services.AddScoped<IPriceRepository, PriceRepository>();
builder.Services.AddScoped<IFavoriteRepository, FavoriteRepository>();

// ── Services ─────────────────────────────────────────────────────────────────
builder.Services.AddScoped<ICardService, CardService>();
builder.Services.AddScoped<IPriceService, PriceService>();
builder.Services.AddScoped<IFavoritesService, FavoritesService>();
builder.Services.AddScoped<IMtgJsonService, MtgJsonService>();
builder.Services.AddScoped<IGoodGamesScraperService, GoodGamesScraperService>();
builder.Services.AddScoped<IDiscordNotificationService, DiscordNotificationService>();

// ── HTTP Clients (named — consumed via IHttpClientFactory) ────────────────────
// Using named clients because the services are Scoped, and AddHttpClient<T> creates
// Transient registrations that would conflict with our Scoped registrations.
builder.Services.AddHttpClient(MtgJsonService.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromMinutes(30); // Large file downloads
    client.DefaultRequestHeaders.Add("User-Agent", "MTGPriceTracker/1.0");
});
builder.Services.AddHttpClient(GoodGamesScraperService.HttpClientName, client =>
{
    client.DefaultRequestHeaders.Add("User-Agent",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0 Safari/537.36");
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHttpClient(DiscordNotificationService.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
});

// ── Background Services ───────────────────────────────────────────────────────
builder.Services.AddSingleton<SyncState>();
builder.Services.AddSingleton<PriceSyncBackgroundService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PriceSyncBackgroundService>());
builder.Services.AddHostedService<DatabaseWarmupService>();

// ── API & Swagger ─────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "MTG Price Tracker API", Version = "v1" });
});

// ── CORS (for local dev if running client separately) ─────────────────────────
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

var app = builder.Build();

// ── Ensure database is created and tuned ─────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();

    // WAL mode persists in the DB file once set, so this is idempotent.
    // Synchronous=NORMAL is safe with WAL and ~2x faster than the default FULL.
    await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL");
    await db.Database.ExecuteSqlRawAsync("PRAGMA synchronous=NORMAL");

    // Schema evolution: add new columns to Cards if they don't exist yet.
    // EnsureCreated won't alter existing tables, so we do it manually.
    // SQLite doesn't support IF NOT EXISTS on ALTER TABLE, so we swallow the error.
    foreach (var sql in new[]
    {
        "ALTER TABLE \"Cards\" ADD COLUMN \"FrameEffects\" TEXT",
        "ALTER TABLE \"Cards\" ADD COLUMN \"BorderColor\"  TEXT",
        // HasNonFoil defaults to 1 (true) so existing cards without data stay valid.
        "ALTER TABLE \"Cards\" ADD COLUMN \"HasNonFoil\"       INTEGER NOT NULL DEFAULT 1",
        "ALTER TABLE \"Cards\" ADD COLUMN \"HasFoil\"          INTEGER NOT NULL DEFAULT 0",
        "ALTER TABLE \"Cards\" ADD COLUMN \"HasEtched\"        INTEGER NOT NULL DEFAULT 0",
        // CollectorNumber: MTGJSON 'number' field. Enables exact SKU-based GG matching.
        "ALTER TABLE \"Cards\" ADD COLUMN \"CollectorNumber\"  TEXT",
    })
    {
        try { await db.Database.ExecuteSqlRawAsync(sql); }
        catch { /* column already exists — safe to ignore */ }
    }
}

// ── Middleware pipeline ───────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "MTG Price Tracker API v1"));
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseCors();
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();
app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();
