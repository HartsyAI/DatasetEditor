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
using DatasetStudio.ClientApp.Services.Interop;
using DatasetStudio.ClientApp.Services.StateManagement;
using DatasetStudio.ClientApp.Shared.Services;
using DatasetStudio.ClientApp.Features.Datasets.Services;
using DatasetStudio.Core.Utilities;
using DatasetStudio.Core.Utilities.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Threading.Tasks;

WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// No bare HttpClient is registered on purpose: every API call goes through a typed client
// (DatasetApiClient / AuthApiClient) so the session cookie handler is attached (audit U-2).

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
    // Auth calls run on every page load from the auth state provider. HttpClient's default
    // 100 s timeout leaves the "Checking access..." screen hanging when the API is down
    // (audit U-9). Dataset calls keep the default because uploads are legitimately long.
    client.Timeout = TimeSpan.FromSeconds(15);
}).AddHttpMessageHandler<CookieCredentialsHandler>();

// MudBlazor services
builder.Services.AddMudServices();

// LocalStorage for browser storage
builder.Services.AddBlazoredLocalStorage();

// Dataset data services. (The Core parser/layout/modality registries, FormatDetector, FilterService
// and SearchService used to be registered here although no UI used them; audit K-2.)
builder.Services.AddScoped<DatasetCacheService>();
builder.Services.AddScoped<IngestionWorkflowService>();
builder.Services.AddScoped<IndexedDbCache>();

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
builder.Services.AddScoped<DatasetState>();
builder.Services.AddScoped<FilterState>();
builder.Services.AddScoped<ViewState>();
builder.Services.AddScoped<ApiKeyState>();

// TODO: Add Fluxor state management when complexity grows
// TODO: Add SignalR services for real-time features (when server added)

WebAssemblyHost host = builder.Build();

// Routes Core.Utilities.Logging.Logs (used across ~25 ClientApp/Core call sites that predate
// real DI-based logging) through WASM's built-in browser-console logging provider instead of
// its Console.WriteLine fallback -- Console output doesn't reach the browser devtools console
// in a WASM app the way it does in a normal .NET process. See Logs.cs for why this project
// isn't fully converted to injected ILogger<T> instead.
Logs.Initialize(host.Services.GetRequiredService<ILoggerFactory>());

await host.RunAsync();
