using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MTGPriceTracker.Client;
using MTGPriceTracker.Client.Services;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// API service with base address pointing to the server
builder.Services.AddHttpClient<ApiService>(client =>
{
    client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);
});

// MudBlazor
builder.Services.AddMudServices();


await builder.Build().RunAsync();