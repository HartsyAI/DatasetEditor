using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using Blazored.LocalStorage;
using DatasetStudio.ClientApp;
using DatasetStudio.ClientApp.Configuration;
using DatasetStudio.ClientApp.Features.Auth.Services;
using DatasetStudio.ClientApp.Services.ApiClients;
using DatasetStudio.ClientApp.Services.Caching;
using DatasetStudio.ClientApp.Services.Extensions;
using DatasetStudio.ClientApp.Services.Interop;
using DatasetStudio.ClientApp.Services.StateManagement;
using DatasetStudio.ClientApp.Shared.Services;
using DatasetStudio.ClientApp.Features.Datasets.Services;
using DatasetStudio.Core.BusinessLogic;
using DatasetStudio.Core.BusinessLogic.Layouts;
using DatasetStudio.Core.BusinessLogic.Parsers;
using DatasetStudio.Core.BusinessLogic.ModalityProviders;
using DatasetStudio.Core.Utilities;
using DatasetStudio.Core.Utilities.Logging;
using DatasetStudio.Extensions.SDK;
using Microsoft.Extensions.Options;
using System.Threading.Tasks;

WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// HTTP Client for future API calls
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Dataset API client configuration. BaseAddress is intentionally optional: the dev-only
// wwwroot/appsettings.Development.json overlay sets it to the API dev-server's cross-origin
// address (client:5002 -> API:5000), but the base wwwroot/appsettings.json (which is what a
// Release publish ships, with no environment overlay) leaves it blank, so a production
// same-container deployment (the API self-hosts the published client -- see Program.cs's
// UseBlazorFrameworkFiles) falls back to the page's own origin below, whatever host/port/
// domain that turns out to be at deploy time, instead of baking in a dev-only absolute URL
// that would silently 404/CORS-fail in any other environment (found via a real trimmed-Release
// smoke test, not assumed).
builder.Services.AddOptions<DatasetApiOptions>()
    .Bind(builder.Configuration.GetSection("DatasetApi"))
    .ValidateOnStart();

// CookieCredentialsHandler makes every request through these typed clients opt into sending
// the session cookie cross-origin (client:5002 dev server -> API:5000) -- without it, calls
// would silently 401 even right after a successful login. Attached once here, not per call
// site, so a new API method can't forget it.
builder.Services.AddTransient<CookieCredentialsHandler>();

Uri ResolveApiBaseAddress(IServiceProvider sp)
{
    DatasetApiOptions options = sp.GetRequiredService<IOptions<DatasetApiOptions>>().Value;
    return string.IsNullOrWhiteSpace(options.BaseAddress)
        ? new Uri(builder.HostEnvironment.BaseAddress)
        : new Uri(options.BaseAddress, UriKind.Absolute);
}

builder.Services.AddHttpClient<DatasetApiClient>((sp, client) =>
{
    client.BaseAddress = ResolveApiBaseAddress(sp);
}).AddHttpMessageHandler<CookieCredentialsHandler>();

builder.Services.AddHttpClient<AuthApiClient>((sp, client) =>
{
    client.BaseAddress = ResolveApiBaseAddress(sp);
}).AddHttpMessageHandler<CookieCredentialsHandler>();

// MudBlazor services
builder.Services.AddMudServices();

// LocalStorage for browser storage
builder.Services.AddBlazoredLocalStorage();

// Register Core services
builder.Services.AddSingleton<ParserRegistry>();
builder.Services.AddSingleton<ModalityProviderRegistry>();
builder.Services.AddSingleton<LayoutRegistry>();
builder.Services.AddScoped<FormatDetector>();
builder.Services.AddScoped<DatasetCacheService>();
builder.Services.AddScoped<IndexedDbCache>();
builder.Services.AddScoped<FilterService>();
builder.Services.AddScoped<SearchService>();

AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
{
    Logs.Error($"Unhandled exception: {args.ExceptionObject}");
};

TaskScheduler.UnobservedTaskException += (sender, args) =>
{
    Logs.Error($"Unobserved task exception: {args.Exception}");
    args.SetObserved();
};

// Auth: AuthorizeRouteView/AuthorizeView/[Authorize] all resolve state through this single
// scoped provider, which asks the API's /api/auth/me (backed by the session cookie -- there is
// no client-side token to decode, the cookie is HttpOnly).
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<CookieAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<CookieAuthenticationStateProvider>());

// Register Client services
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<NavigationService>();
builder.Services.AddScoped<ItemEditService>();
builder.Services.AddScoped<IndexedDbInterop>();
builder.Services.AddScoped<ImageUrlHelper>();

// Register State Management
builder.Services.AddScoped<AppState>();
builder.Services.AddScoped<DatasetState>();
builder.Services.AddScoped<FilterState>();
builder.Services.AddScoped<ViewState>();
builder.Services.AddScoped<ApiKeyState>();

// TODO: Add Fluxor state management when complexity grows
// TODO: Add SignalR services for real-time features (when server added)

await builder.Build().RunAsync();
