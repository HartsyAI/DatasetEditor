# Dataset Studio Extension Development Guide

## Table of Contents

1. [Extension Architecture](#extension-architecture)
2. [API vs Client vs Shared](#api-vs-client-vs-shared)
3. [Creating Your First Extension](#creating-your-first-extension)
4. [Manifest File Format](#manifest-file-format)
5. [Extension Lifecycle](#extension-lifecycle)
6. [API/Client Communication](#api-client-communication)
7. [Deployment Scenarios](#deployment-scenarios)
8. [Security and Permissions](#security-and-permissions)
9. [Testing Extensions](#testing-extensions)
10. [Publishing Extensions](#publishing-extensions)

---

## Extension Architecture

Dataset Studio uses a **distributed extension system** designed for scenarios where the API backend and Blazor WebAssembly client run on different servers.

### Core Principles

1. **Separation of Concerns**: Extensions are split into API (server-side) and Client (browser-side) components
2. **Independent Deployment**: API and Client can be deployed to different servers
3. **Type-Safe Communication**: Shared DTOs ensure type safety across API/Client boundary
4. **Dynamic Loading**: Extensions are discovered and loaded at runtime
5. **Isolated Execution**: Each extension runs in its own context

### Architecture Diagram

```
┌─────────────────────────────────────────────────────────────┐
│                     Extension System                         │
├─────────────────────────────────────────────────────────────┤
│                                                               │
│  ┌─────────────────────┐         ┌─────────────────────┐   │
│  │   API Server        │         │   Client (Browser)   │   │
│  │   (ASP.NET Core)    │ ◄─HTTP─►│   (Blazor WASM)     │   │
│  └─────────────────────┘         └─────────────────────┘   │
│           │                                 │                │
│           │                                 │                │
│  ┌────────▼────────────┐         ┌─────────▼───────────┐   │
│  │ ApiExtensionRegistry│         │ClientExtensionRegistry│  │
│  └────────┬────────────┘         └─────────┬───────────┘   │
│           │                                 │                │
│  ┌────────▼────────────┐         ┌─────────▼───────────┐   │
│  │  Extension Loader   │         │  Extension Loader   │   │
│  └────────┬────────────┘         └─────────┬───────────┘   │
│           │                                 │                │
│  ┌────────▼────────────┐         ┌─────────▼───────────┐   │
│  │  Extensions/*.Api   │         │ Extensions/*.Client │   │
│  │  - CoreViewer.Api   │         │ - CoreViewer.Client │   │
│  │  - AITools.Api      │         │ - AITools.Client    │   │
│  │  - Editor.Api       │         │ - Editor.Client     │   │
│  └─────────────────────┘         └─────────────────────┘   │
│                                                               │
└─────────────────────────────────────────────────────────────┘
```

---

## API vs Client vs Shared

### When to Use Each Component

#### API Component (ExtensionName.Api)

**Use for:**
- Database operations
- File system access
- External API calls (HuggingFace, OpenAI, etc.)
- Background processing
- Heavy computations
- Data processing pipelines

**Example: AITools.Api**
```csharp
public class AIToolsApiExtension : BaseApiExtension
{
    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<HuggingFaceClient>();
        services.AddHostedService<BatchCaptioningWorker>();
    }

    public void RegisterEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/extensions/aitools/caption",
            async (CaptionRequest req) =>
            {
                // Call HuggingFace API server-side
                var caption = await CaptionImage(req.ImageUrl);
                return Results.Ok(new CaptionResponse { Caption = caption });
            });
    }
}
```

#### Client Component (ExtensionName.Client)

**Use for:**
- Blazor UI components
- Client-side state management
- Browser interactions
- Real-time UI updates
- Client-side validation
- Local storage access

**Example: AITools.Client**
```csharp
public class AIToolsClientExtension : BaseClientExtension
{
    public override void RegisterComponents()
    {
        // Register Blazor components
        // Components: CaptionTool.razor, TaggingTool.razor
    }

    // Call API endpoint from client
    public async Task<string> CaptionImageAsync(string imageUrl)
    {
        var request = new CaptionRequest { ImageUrl = imageUrl };
        var response = await PostAsync<CaptionRequest, CaptionResponse>(
            "/caption", request);
        return response?.Caption ?? "";
    }
}
```

#### Shared Component (ExtensionName.Shared)

**Use for:**
- Data Transfer Objects (DTOs)
- Request/Response models
- Enums and constants
- Validation attributes
- Shared business logic (minimal)

**Example: AITools.Shared**
```csharp
namespace DatasetStudio.Extensions.AITools.Shared.Models;

public class CaptionRequest
{
    public required string ImageUrl { get; set; }
    public string? Model { get; set; }
}

public class CaptionResponse
{
    public required string Caption { get; set; }
    public double Confidence { get; set; }
}
```

---

## Creating Your First Extension

### Step 1: Create Project Structure

```bash
mkdir -p Extensions/BuiltIn/MyExtension/MyExtension.Api
mkdir -p Extensions/BuiltIn/MyExtension/MyExtension.Client
mkdir -p Extensions/BuiltIn/MyExtension/MyExtension.Shared
```

### Step 2: Create Manifest File

**Extensions/BuiltIn/MyExtension/extension.manifest.json:**

```json
{
  "schemaVersion": 1,
  "metadata": {
    "id": "MyExtension",
    "name": "My Extension",
    "version": "1.0.0",
    "description": "Description of what your extension does",
    "author": "Your Name",
    "license": "MIT"
  },
  "deploymentTarget": "Both",
  "requiredPermissions": [
    "datasets.read",
    "datasets.write"
  ],
  "apiEndpoints": [
    {
      "method": "POST",
      "route": "/api/extensions/myextension/process",
      "handlerType": "MyExtension.Api.ProcessHandler",
      "description": "Process data"
    }
  ],
  "navigationItems": [
    {
      "text": "My Extension",
      "route": "/myextension",
      "icon": "mdi-star",
      "order": 100
    }
  ]
}
```

### Step 3: Implement API Extension

**MyExtension.Api/MyExtensionApiExtension.cs:**

```csharp
using DatasetStudio.Extensions.SDK;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace MyExtension.Api;

public class MyExtensionApiExtension : BaseApiExtension, IExtensionApiEndpoint
{
    public override ExtensionManifest GetManifest()
    {
        // Load from extension.manifest.json
        return ExtensionManifest.LoadFromDirectory("Extensions/BuiltIn/MyExtension");
    }

    public override void ConfigureServices(IServiceCollection services)
    {
        // Register your services
        services.AddScoped<IMyService, MyService>();

        base.ConfigureServices(services);
    }

    public string GetBasePath() => "/api/extensions/myextension";

    public void RegisterEndpoints(IEndpointRouteBuilder endpoints)
    {
        var basePath = GetBasePath();

        endpoints.MapPost($"{basePath}/process", async (ProcessRequest req) =>
        {
            // Your logic here
            return Results.Ok(new ProcessResponse { Result = "Success" });
        });
    }

    public IReadOnlyList<ApiEndpointDescriptor> GetEndpointDescriptors()
    {
        return new List<ApiEndpointDescriptor>
        {
            new() { Method = "POST", Route = "/process", HandlerType = "MyExtensionApiExtension" }
        };
    }
}
```

### Step 4: Implement Client Extension

**MyExtension.Client/MyExtensionClientExtension.cs:**

```csharp
using DatasetStudio.Extensions.SDK;
using Microsoft.Extensions.DependencyInjection;

namespace MyExtension.Client;

public class MyExtensionClientExtension : BaseClientExtension
{
    public override ExtensionManifest GetManifest()
    {
        return ExtensionManifest.LoadFromDirectory("Extensions/BuiltIn/MyExtension");
    }

    public override void ConfigureServices(IServiceCollection services)
    {
        // Register client services
        services.AddScoped<MyExtensionViewModel>();

        base.ConfigureServices(services);
    }

    public override void RegisterComponents()
    {
        // Blazor components are auto-discovered
        base.RegisterComponents();
    }

    public override void RegisterNavigation()
    {
        // Navigation items from manifest are auto-registered
        base.RegisterNavigation();
    }

    // Helper method to call API
    public async Task<string> ProcessAsync(string data)
    {
        var request = new ProcessRequest { Data = data };
        var response = await PostAsync<ProcessRequest, ProcessResponse>("/process", request);
        return response?.Result ?? "";
    }
}
```

### Step 5: Create Blazor Component

**MyExtension.Client/Pages/MyExtensionPage.razor:**

```razor
@page "/myextension"
@using MyExtension.Shared.Models
@inject MyExtensionClientExtension Extension

<MudContainer>
    <MudText Typo="Typo.h3">My Extension</MudText>

    <MudTextField @bind-Value="inputData" Label="Input Data" />
    <MudButton OnClick="ProcessDataAsync" Color="Color.Primary">Process</MudButton>

    @if (!string.IsNullOrEmpty(result))
    {
        <MudAlert Severity="Severity.Success">@result</MudAlert>
    }
</MudContainer>

@code {
    private string inputData = "";
    private string result = "";

    private async Task ProcessDataAsync()
    {
        result = await Extension.ProcessAsync(inputData);
    }
}
```

### Step 6: Define Shared Models

**MyExtension.Shared/Models/ProcessModels.cs:**

```csharp
namespace MyExtension.Shared.Models;

public class ProcessRequest
{
    public required string Data { get; set; }
}

public class ProcessResponse
{
    public required string Result { get; set; }
}
```

---

## Manifest File Format

The manifest file (`extension.manifest.json`) is the heart of your extension.

### Complete Example

```json
{
  "schemaVersion": 1,
  "metadata": {
    "id": "MyExtension",
    "name": "My Extension Name",
    "version": "1.2.3",
    "description": "Detailed description",
    "author": "Author Name",
    "license": "MIT",
    "homepage": "https://github.com/author/myextension",
    "repository": "https://github.com/author/myextension",
    "tags": ["tag1", "tag2"],
    "categories": ["Editing", "AI/ML"]
  },
  "deploymentTarget": "Both",
  "dependencies": {
    "CoreViewer": ">=1.0.0",
    "Editor": "^2.0.0"
  },
  "requiredPermissions": [
    "datasets.read",
    "datasets.write",
    "filesystem.read",
    "network.external"
  ],
  "apiEndpoints": [
    {
      "method": "GET|POST|PUT|DELETE|PATCH",
      "route": "/api/extensions/{extensionId}/endpoint",
      "handlerType": "Fully.Qualified.Type.Name",
      "description": "What this endpoint does",
      "requiresAuth": true
    }
  ],
  "blazorComponents": {
    "ComponentName": "Fully.Qualified.Component.Type"
  },
  "navigationItems": [
    {
      "text": "Menu Text",
      "route": "/route",
      "icon": "mdi-icon-name",
      "order": 100,
      "parentId": "optional-parent",
      "requiredPermission": "permission.name"
    }
  ],
  "backgroundWorkers": [
    {
      "id": "WorkerId",
      "typeName": "Fully.Qualified.Worker.Type",
      "description": "What this worker does",
      "autoStart": true
    }
  ],
  "databaseMigrations": [
    "Migration.Fully.Qualified.Name"
  ],
  "configurationSchema": "JSON Schema for configuration validation",
  "defaultConfiguration": {
    "setting1": "value1",
    "setting2": 42
  }
}
```

### Deployment Targets

- **`"Api"`**: Extension runs only on API server
- **`"Client"`**: Extension runs only in browser
- **`"Both"`**: Extension has both API and Client components

---

## Extension Lifecycle

### 1. Discovery Phase

```
ApiExtensionRegistry.DiscoverAndLoadAsync()
  → Scan Extensions/BuiltIn directory
  → Find extension.manifest.json files
  → Parse and validate manifests
  → Filter by deployment target (Api or Both)
```

### 2. Dependency Resolution

```
  → Build dependency graph
  → Check for circular dependencies
  → Topological sort for load order
```

### 3. Loading Phase

```
For each extension in load order:
  → Load assembly (ExtensionName.Api.dll)
  → Find type implementing IExtension
  → Create instance
  → Call ConfigureServices(IServiceCollection)
```

### 4. Configuration Phase

```
After app.Build():
  → Call ConfigureApp(IApplicationBuilder)
  → Create ExtensionContext
  → Call InitializeAsync(IExtensionContext)
  → Call ValidateAsync()
```

### 5. Runtime Phase

```
Extension is active:
  → Endpoints handle requests
  → Background workers run
  → Health checks monitor status
```

### 6. Shutdown Phase

```
On application shutdown:
  → Call Dispose() on each extension
  → Clean up resources
  → Unload assemblies (if collectible)
```

---

## API/Client Communication

### Pattern: Client calls API

**Client Extension:**
```csharp
public class MyClientExtension : BaseClientExtension
{
    public async Task<DataResult> GetDataAsync()
    {
        // Built-in helper automatically constructs URL
        // Calls: /api/extensions/myextension/data
        return await GetAsync<DataResult>("/data");
    }
}
```

**API Extension:**
```csharp
public class MyApiExtension : BaseApiExtension, IExtensionApiEndpoint
{
    public void RegisterEndpoints(IEndpointRouteBuilder endpoints)
    {
        var basePath = GetBasePath(); // /api/extensions/myextension

        endpoints.MapGet($"{basePath}/data", async () =>
        {
            var data = await FetchDataAsync();
            return Results.Ok(data);
        });
    }
}
```

### Using ExtensionApiClient

For complex scenarios:

```csharp
public class MyClientExtension : BaseClientExtension
{
    private ExtensionApiClient? _apiClient;

    protected override Task OnInitializeAsync()
    {
        _apiClient = new ExtensionApiClient(
            Context.ApiClient!,
            "myextension",
            Logger);
        return Task.CompletedTask;
    }

    public async Task<Result> ProcessFileAsync(Stream file, string fileName)
    {
        return await _apiClient.UploadFileAsync<Result>(
            "/process",
            file,
            fileName,
            additionalData: new Dictionary<string, string>
            {
                ["option1"] = "value1"
            });
    }
}
```

---

## Deployment Scenarios

### Scenario 1: Single Server (Development)

Both API and Client on same machine:

```
http://localhost:5001 (API + Client)
  → Extensions loaded on server
  → Blazor WASM served from wwwroot
  → API calls to localhost
```

**Configuration:**
```json
// appsettings.Development.json (both API and Client)
{
  "Api": {
    "BaseUrl": "http://localhost:5001"
  },
  "Extensions": {
    "Enabled": true,
    "Directory": "./Extensions/BuiltIn"
  }
}
```

### Scenario 2: Distributed Deployment (Production)

API and Client on different servers:

```
https://api.myapp.com (API Server)
  → Loads *.Api.dll extensions
  → Exposes REST endpoints

https://app.myapp.com (Client CDN)
  → Loads *.Client.dll extensions
  → Renders Blazor UI
  → Calls api.myapp.com for data
```

**API Configuration:**
```json
{
  "Extensions": {
    "Directory": "/var/www/extensions"
  },
  "Cors": {
    "AllowedOrigins": ["https://app.myapp.com"]
  }
}
```

**Client Configuration:**
```json
{
  "Api": {
    "BaseUrl": "https://api.myapp.com"
  },
  "Extensions": {
    "Enabled": true
  }
}
```

### Scenario 3: Cloud Deployment

```
Azure/AWS API
  → API extensions in container
  → Scales independently

Azure CDN / CloudFront
  → Client WASM files cached globally
  → Fast worldwide access
```

---

## Security and Permissions

### Permission System

Extensions declare required permissions in manifest:

```json
"requiredPermissions": [
  "datasets.read",
  "datasets.write",
  "filesystem.write",
  "network.external",
  "ai.huggingface"
]
```

### Validating Permissions

```csharp
protected override async Task<bool> OnValidateAsync()
{
    // Check if required permissions are granted
    var hasPermission = await CheckPermissionAsync("datasets.write");
    if (!hasPermission)
    {
        Logger.LogError("Missing required permission: datasets.write");
        return false;
    }
    return true;
}
```

### Secure Configuration

Use secrets for sensitive data:

```csharp
protected override async Task OnInitializeAsync()
{
    var apiKey = Context.Configuration["HuggingFaceApiKey"];
    if (string.IsNullOrEmpty(apiKey))
    {
        throw new InvalidOperationException("API key not configured");
    }

    _huggingFaceClient = new HuggingFaceClient(apiKey);
}
```

Store secrets in:
- **Development**: User Secrets (`dotnet user-secrets`)
- **Production**: Environment variables, Key Vault, etc.

---

## Testing Extensions

### Unit Testing

Test extension logic independently:

```csharp
public class MyExtensionTests
{
    [Fact]
    public async Task ProcessAsync_ReturnsExpectedResult()
    {
        // Arrange
        var extension = new MyExtensionApiExtension();
        var mockService = new Mock<IMyService>();
        // ... setup

        // Act
        var result = await extension.ProcessDataAsync("test");

        // Assert
        Assert.Equal("expected", result);
    }
}
```

### Integration Testing

Test API/Client communication:

```csharp
public class ExtensionIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ExtensionIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ApiEndpoint_ReturnsSuccess()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/extensions/myextension/process",
            new ProcessRequest { Data = "test" });

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ProcessResponse>();
        Assert.NotNull(result);
    }
}
```

---

## Publishing Extensions

### Built-In Extensions

1. Add to `Extensions/BuiltIn/`
2. Include in project references
3. Deploy with application

### User Extensions

1. Package as NuGet
2. Users install to `Extensions/User/`
3. Auto-discovered on startup

### Extension Package Structure

```
MyExtension.1.0.0.nupkg
├── lib/
│   ├── net8.0/
│   │   ├── MyExtension.Api.dll
│   │   ├── MyExtension.Client.dll
│   │   └── MyExtension.Shared.dll
├── content/
│   └── Extensions/User/MyExtension/
│       └── extension.manifest.json
└── MyExtension.nuspec
```

---

## Best Practices

1. **Keep it Simple**: Start with minimal functionality
2. **Test Thoroughly**: Unit and integration tests
3. **Document APIs**: Add XML comments and OpenAPI docs
4. **Version Carefully**: Follow semantic versioning
5. **Handle Errors**: Graceful degradation
6. **Log Appropriately**: Use structured logging
7. **Respect Permissions**: Only request what you need
8. **Optimize Performance**: Cache, batch, async
9. **Support Distributed**: Always assume API ≠ Client host

---

## Support and Resources

- **GitHub**: https://github.com/datasetstudio/extensions
- **Documentation**: https://docs.datasetstudio.com
- **Community**: https://discord.gg/datasetstudio
- **Examples**: See `Extensions/BuiltIn/` for reference implementations
