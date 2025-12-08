using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using Blazored.LocalStorage;
using HartsysDatasetEditor.Client;
using HartsysDatasetEditor.Client.Services;
using HartsysDatasetEditor.Client.Services.Api;
using HartsysDatasetEditor.Client.Services.JsInterop;
using HartsysDatasetEditor.Client.Services.StateManagement;
using HartsysDatasetEditor.Core.Services;
using HartsysDatasetEditor.Core.Services.Layouts;
using HartsysDatasetEditor.Core.Services.Parsers;
using HartsysDatasetEditor.Core.Services.Providers;
using HartsysDatasetEditor.Core.Utilities;
using Microsoft.Extensions.Options;
using System.Threading.Tasks;

WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// HTTP Client for future API calls
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Dataset API client configuration
builder.Services.AddOptions<DatasetApiOptions>()
    .Bind(builder.Configuration.GetSection("DatasetApi"))
    .Validate(options => !string.IsNullOrWhiteSpace(options.BaseAddress), "DatasetApi:BaseAddress must be configured.")
    .ValidateOnStart();

builder.Services.AddHttpClient<DatasetApiClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<DatasetApiOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseAddress!, UriKind.Absolute);
});

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
builder.Services.AddScoped<DatasetIndexedDbCache>();
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
// TODO: Add authentication services when server is added
// TODO: Add SignalR services for real-time features (when server added)

await builder.Build().RunAsync();
