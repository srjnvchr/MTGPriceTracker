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
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

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

// ── Background Services ───────────────────────────────────────────────────────
builder.Services.AddSingleton<SyncState>();
builder.Services.AddSingleton<PriceSyncBackgroundService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PriceSyncBackgroundService>());

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

// ── Ensure database is created ────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
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
