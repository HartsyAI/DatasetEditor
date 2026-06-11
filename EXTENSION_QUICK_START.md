# Extension Quick Start Guide

## Creating Your First Extension

This guide walks you through creating a basic extension for Dataset Studio.

## Prerequisites

- .NET 8.0 SDK
- Understanding of ASP.NET Core and Blazor
- Dataset Studio source code

## Step 1: Create Extension Manifest

Create `extension.manifest.json` in your extension directory:

```json
{
  "schemaVersion": 1,
  "metadata": {
    "id": "MyExtension",
    "name": "My First Extension",
    "version": "1.0.0",
    "description": "A sample extension",
    "author": "Your Name"
  },
  "deploymentTarget": "Both",
  "dependencies": {},
  "requiredPermissions": []
}
```

## Step 2: Create API Extension (Optional)

Create `MyExtension.Api/MyExtensionApiExtension.cs`:

```csharp
using DatasetStudio.Extensions.SDK;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;

namespace MyExtension.Api;

public class MyExtensionApiExtension : BaseApiExtension
{
    private ExtensionManifest? _manifest;

    public override ExtensionManifest GetManifest()
    {
        if (_manifest == null)
        {
            var manifestPath = Path.Combine(
                Context.ExtensionDirectory,
                "extension.manifest.json");
            _manifest = ExtensionManifest.LoadFromFile(manifestPath);
        }
        return _manifest;
    }

    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        // Register your services
        services.AddScoped<IMyService, MyService>();
    }

    protected override void OnConfigureApp(IApplicationBuilder app)
    {
        base.OnConfigureApp(app);

        // Register your API endpoints
        if (app is IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/extensions/MyExtension/hello",
                () => Results.Ok(new { message = "Hello from MyExtension!" }));
        }
    }

    protected override async Task OnInitializeAsync()
    {
        Logger.LogInformation("MyExtension API initializing...");

        // Your initialization logic here

        await Task.CompletedTask;
    }

    protected override async Task<bool> OnValidateAsync()
    {
        // Validate your extension is properly configured
        return await Task.FromResult(true);
    }
}
```

## Step 3: Create Client Extension (Optional)

Create `MyExtension.Client/MyExtensionClientExtension.cs`:

```csharp
using DatasetStudio.Extensions.SDK;
using Microsoft.Extensions.DependencyInjection;

namespace MyExtension.Client;

public class MyExtensionClientExtension : BaseClientExtension
{
    private ExtensionManifest? _manifest;

    public override ExtensionManifest GetManifest()
    {
        if (_manifest == null)
        {
            // In WASM, embed manifest as resource or hardcode
            var manifestJson = @"{
                ""schemaVersion"": 1,
                ""metadata"": {
                    ""id"": ""MyExtension"",
                    ""name"": ""My First Extension"",
                    ""version"": ""1.0.0""
                },
                ""deploymentTarget"": ""Client""
            }";
            _manifest = ExtensionManifest.LoadFromJson(manifestJson);
        }
        return _manifest;
    }

    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        // Register client services
        services.AddScoped<IMyClientService, MyClientService>();
    }

    protected override async Task OnInitializeAsync()
    {
        Logger.LogInformation("MyExtension Client initializing...");

        // Test API connectivity
        try
        {
            var response = await GetAsync<object>("/hello");
            Logger.LogInformation("API connection successful");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to connect to API");
        }
    }
}
```

## Step 4: Create Blazor Component (Client)

Create `MyExtension.Client/Components/MyComponent.razor`:

```razor
@page "/myextension"
@inject IMyClientService MyService

<MudContainer>
    <MudText Typo="Typo.h3">My Extension</MudText>

    <MudButton OnClick="CallApi" Color="Color.Primary">
        Call API
    </MudButton>

    @if (!string.IsNullOrEmpty(message))
    {
        <MudAlert Severity="Severity.Success">@message</MudAlert>
    }
</MudContainer>

@code {
    private string? message;

    private async Task CallApi()
    {
        // Extension automatically available via DI
        message = await MyService.GetMessageFromApi();
    }
}
```

## Step 5: Build Extension Assemblies

### API Assembly
```bash
cd MyExtension.Api
dotnet build -c Release
# Output: MyExtension.Api.dll
```

### Client Assembly
```bash
cd MyExtension.Client
dotnet build -c Release
# Output: MyExtension.Client.dll
```

## Step 6: Deploy Extension

Copy files to extension directory:

```
Extensions/
└── BuiltIn/
    └── MyExtension/
        ├── extension.manifest.json
        ├── MyExtension.Api.dll      (if deploymentTarget: Api or Both)
        └── MyExtension.Client.dll   (if deploymentTarget: Client or Both)
```

## Step 7: Configure Application

### API Server (`appsettings.json`)
```json
{
  "Extensions": {
    "Enabled": true,
    "Directory": "./Extensions/BuiltIn",
    "UserDirectory": "./Extensions/User"
  }
}
```

### Client (`appsettings.json`)
```json
{
  "Extensions": {
    "Enabled": true,
    "Directory": "./Extensions/BuiltIn"
  },
  "Api": {
    "BaseUrl": "https://localhost:7000"
  }
}
```

## Step 8: Test Extension

1. Start API server
2. Start Client app
3. Navigate to `/myextension`
4. Check logs for extension loading messages

## Common Patterns

### Calling API from Client

```csharp
public class MyClientService
{
    private readonly HttpClient _apiClient;

    public MyClientService(IHttpClientFactory httpClientFactory)
    {
        _apiClient = httpClientFactory.CreateClient("Extension_MyExtension");
    }

    public async Task<string> GetMessageFromApi()
    {
        var response = await _apiClient.GetFromJsonAsync<MessageResponse>(
            "/api/extensions/MyExtension/hello");
        return response?.Message ?? "No message";
    }
}
```

### Using Configuration

```csharp
protected override async Task OnInitializeAsync()
{
    // Access extension-specific config
    var apiKey = Context.Configuration["ApiKey"];
    var timeout = Context.Configuration.GetValue<int>("Timeout", 30);

    if (string.IsNullOrEmpty(apiKey))
    {
        Logger.LogWarning("API key not configured");
    }
}
```

In `appsettings.json`:
```json
{
  "Extensions": {
    "MyExtension": {
      "ApiKey": "your-api-key",
      "Timeout": 60
    }
  }
}
```

### Registering Background Services (API)

```csharp
public override void ConfigureServices(IServiceCollection services)
{
    base.ConfigureServices(services);

    // Register background worker
    AddBackgroundService<MyBackgroundWorker>(services);
}

public class MyBackgroundWorker : BackgroundService
{
    private readonly ILogger<MyBackgroundWorker> _logger;

    public MyBackgroundWorker(ILogger<MyBackgroundWorker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            await Task.Delay(10000, stoppingToken);
        }
    }
}
```

### Health Checks

```csharp
protected override async Task<ExtensionHealthStatus> OnGetHealthAsync()
{
    try
    {
        // Check your extension's health
        var isHealthy = await CheckDatabaseAsync();

        return new ExtensionHealthStatus
        {
            Health = isHealthy ? ExtensionHealth.Healthy : ExtensionHealth.Degraded,
            Message = isHealthy ? "All systems operational" : "Database slow",
            Details = new Dictionary<string, object>
            {
                ["DatabaseConnected"] = isHealthy,
                ["ResponseTime"] = "50ms"
            }
        };
    }
    catch (Exception ex)
    {
        return new ExtensionHealthStatus
        {
            Health = ExtensionHealth.Unhealthy,
            Message = $"Health check failed: {ex.Message}"
        };
    }
}
```

### Custom Validation

```csharp
protected override async Task<bool> OnValidateAsync()
{
    // Check required configuration
    var apiKey = Context.Configuration["ApiKey"];
    if (string.IsNullOrEmpty(apiKey))
    {
        Logger.LogError("ApiKey is required but not configured");
        return false;
    }

    // Check required services
    var myService = Context.Services.GetService<IMyService>();
    if (myService == null)
    {
        Logger.LogError("IMyService not registered");
        return false;
    }

    // Check external dependencies
    try
    {
        await myService.TestConnectionAsync();
    }
    catch (Exception ex)
    {
        Logger.LogError(ex, "Failed to connect to external service");
        return false;
    }

    return true;
}
```

## Deployment Targets Explained

### Api Only
Use when your extension only needs server-side logic.

```json
{
  "deploymentTarget": "Api"
}
```

Examples:
- Background data processing
- Database migrations
- File system operations
- External API integrations without UI

### Client Only
Use when your extension only needs client-side logic.

```json
{
  "deploymentTarget": "Client"
}
```

Examples:
- UI components
- Client-side visualizations
- Browser interactions
- Local storage management

### Both
Use when you need both server logic and client UI.

```json
{
  "deploymentTarget": "Both"
}
```

Examples:
- AI tools (API for model inference, Client for UI)
- Data editor (API for persistence, Client for editing UI)
- Image processing (API for processing, Client for preview)

## Debugging Tips

### Enable Debug Logging

```json
{
  "Logging": {
    "LogLevel": {
      "DatasetStudio.APIBackend.Services.Extensions": "Debug",
      "DatasetStudio.ClientApp.Services.Extensions": "Debug",
      "Extension.MyExtension": "Debug"
    }
  }
}
```

### Check Extension Loading

Look for these log messages:
```
[Information] Discovering API extensions...
[Information] Found 1 API extensions to load
[Information] Loading extension: MyExtension
[Debug] Loading assembly: MyExtension.Api.dll
[Debug] Found extension type: MyExtension.Api.MyExtensionApiExtension
[Information] Extension loaded successfully: MyExtension
[Information] Configuring extension: MyExtension
[Information] Extension configured successfully: MyExtension
```

### Common Issues

1. **Assembly not found**
   - Check DLL is in correct directory
   - Verify naming convention: `{ExtensionId}.Api.dll` or `{ExtensionId}.Client.dll`
   - Ensure manifest `id` matches assembly name

2. **No IExtension implementation found**
   - Verify class implements IExtension or inherits from BaseApiExtension/BaseClientExtension
   - Check class is public and not abstract

3. **Extension validation failed**
   - Check logs for validation error details
   - Verify required configuration is present
   - Check OnValidateAsync() implementation

4. **HttpClient not configured (Client)**
   - Verify Api:BaseUrl is set in appsettings.json
   - Check HttpClient factory is configured

## Next Steps

1. Review `PHASE_3.1_EXTENSION_LOADING_COMPLETE.md` for complete architecture
2. Review `EXTENSION_ARCHITECTURE.md` for system diagrams
3. Look at built-in extensions for examples:
   - CoreViewer: Basic dataset viewing
   - AITools: API integration example
   - Editor: Complex UI example

## API Reference

### IExtension Methods
- `GetManifest()` - Return extension manifest
- `InitializeAsync(context)` - Initialize extension
- `ConfigureServices(services)` - Register DI services
- `ConfigureApp(app)` - Configure middleware (API only)
- `ValidateAsync()` - Validate configuration
- `GetHealthAsync()` - Return health status
- `Dispose()` - Clean up resources

### BaseApiExtension Helpers
- `AddBackgroundService<T>(services)` - Register background worker
- `AddScoped<T, TImpl>(services)` - Register scoped service
- `AddSingleton<T, TImpl>(services)` - Register singleton
- `AddTransient<T, TImpl>(services)` - Register transient
- `RegisterEndpoints(endpoints)` - Register API endpoints

### BaseClientExtension Helpers
- `GetAsync<T>(endpoint)` - Make GET request to API
- `PostAsync<TReq, TRes>(endpoint, request)` - Make POST request
- `PutAsync<TReq, TRes>(endpoint, request)` - Make PUT request
- `DeleteAsync(endpoint)` - Make DELETE request
- `RegisterComponents()` - Register Blazor components
- `RegisterNavigation()` - Register menu items

### IExtensionContext Properties
- `Manifest` - Extension manifest
- `Services` - Service provider
- `Configuration` - Extension configuration
- `Logger` - Extension logger
- `Environment` - Api or Client
- `ApiClient` - HTTP client (Client only)
- `ExtensionDirectory` - Extension root directory
- `Data` - Extension state dictionary

## License

This extension system is part of Dataset Studio and follows the same license.
