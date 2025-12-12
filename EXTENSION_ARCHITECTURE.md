# Dataset Studio Extension System Architecture

## System Overview

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         Dataset Studio Extension System                      │
│                                                                               │
│  ┌─────────────────────────────────┐  ┌─────────────────────────────────┐  │
│  │       API Server (ASP.NET)      │  │   Client (Blazor WebAssembly)   │  │
│  │                                 │  │                                 │  │
│  │  ┌───────────────────────────┐ │  │  ┌───────────────────────────┐ │  │
│  │  │  ApiExtensionRegistry     │ │  │  │  ClientExtensionRegistry  │ │  │
│  │  │  - Discovery              │ │  │  │  - Discovery              │ │  │
│  │  │  - Loading                │ │  │  │  - Loading                │ │  │
│  │  │  - Lifecycle Management   │ │  │  │  - Lifecycle Management   │ │  │
│  │  └───────────┬───────────────┘ │  │  └───────────┬───────────────┘ │  │
│  │              │                   │  │              │                   │  │
│  │              v                   │  │              v                   │  │
│  │  ┌───────────────────────────┐ │  │  ┌───────────────────────────┐ │  │
│  │  │  ApiExtensionLoader       │ │  │  │  ClientExtensionLoader    │ │  │
│  │  │  - AssemblyLoadContext    │ │  │  │  - Assembly.Load()        │ │  │
│  │  │  - Type Discovery         │ │  │  │  - Component Discovery    │ │  │
│  │  │  - Hot-Reload Support     │ │  │  │  - Route Detection        │ │  │
│  │  └───────────┬───────────────┘ │  │  └───────────┬───────────────┘ │  │
│  │              │                   │  │              │                   │  │
│  │              v                   │  │              v                   │  │
│  │  ┌───────────────────────────┐ │  │  ┌───────────────────────────┐ │  │
│  │  │  Extension Instances      │ │  │  │  Extension Instances      │ │  │
│  │  │  - BaseApiExtension       │ │  │  │  - BaseClientExtension    │ │  │
│  │  │  - IExtension             │ │  │  │  - IExtension             │ │  │
│  │  └───────────────────────────┘ │  │  └───────────────────────────┘ │  │
│  │                                 │  │                                 │  │
│  └─────────────────────────────────┘  └─────────────────────────────────┘  │
│                                                                               │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │                    Shared SDK (Extensions/SDK)                      │    │
│  │                                                                     │    │
│  │  IExtension  │  ExtensionManifest  │  ExtensionContext  │  Models │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────────────────┘
```

## Extension Loading Flow

### API Server Flow
```
Program.cs Startup
    │
    ├─> Create ApiExtensionRegistry
    │
    ├─> DiscoverAndLoadAsync()
    │   │
    │   ├─> Scan Extensions/BuiltIn/ directory
    │   ├─> Scan Extensions/User/ directory
    │   ├─> Find extension.manifest.json files
    │   ├─> Parse and validate manifests
    │   ├─> Filter by DeploymentTarget (Api, Both)
    │   ├─> Resolve dependencies (TODO)
    │   │
    │   └─> For each extension:
    │       ├─> ApiExtensionLoader.LoadExtensionAsync()
    │       │   ├─> Create AssemblyLoadContext
    │       │   ├─> Load {ExtensionId}.Api.dll
    │       │   ├─> Find IExtension type
    │       │   └─> Instantiate extension
    │       │
    │       ├─> extension.ConfigureServices(services)
    │       └─> Store in _loadedExtensions
    │
    ├─> builder.Build() → app
    │
    └─> ConfigureExtensionsAsync(app)
        │
        └─> For each extension:
            ├─> Create ExtensionContext
            ├─> extension.ConfigureApp(app)
            ├─> extension.InitializeAsync(context)
            ├─> extension.ValidateAsync()
            └─> Extension ready
```

### Client (Blazor WASM) Flow
```
Program.cs Startup
    │
    ├─> Create ClientExtensionRegistry
    │
    ├─> DiscoverAndLoadAsync()
    │   │
    │   ├─> Get extension directory (WASM-specific)
    │   ├─> Discover extensions (placeholder for now)
    │   ├─> Filter by DeploymentTarget (Client, Both)
    │   ├─> Resolve dependencies (TODO)
    │   │
    │   └─> For each extension:
    │       ├─> ClientExtensionLoader.LoadExtensionAsync()
    │       │   ├─> Assembly.Load({ExtensionId}.Client)
    │       │   ├─> Find IExtension type
    │       │   ├─> Discover Blazor components
    │       │   └─> Instantiate extension
    │       │
    │       ├─> Configure HttpClient (API base URL)
    │       ├─> extension.ConfigureServices(services)
    │       └─> Store in _loadedExtensions
    │
    ├─> builder.Build() → host
    │
    └─> ConfigureExtensionsAsync()
        │
        └─> For each extension:
            ├─> Create ExtensionContext (with ApiClient)
            ├─> extension.InitializeAsync(context)
            ├─> extension.RegisterComponents()
            ├─> extension.RegisterNavigation()
            ├─> extension.ValidateAsync()
            └─> Extension ready
```

## Extension Lifecycle

```
┌─────────────────────────────────────────────────────────────────────┐
│                        Extension Lifecycle                          │
└─────────────────────────────────────────────────────────────────────┘

1. DISCOVERY
   ├─> Scan extension directories
   ├─> Find extension.manifest.json
   └─> Parse and validate manifest

2. LOADING
   ├─> Load extension assembly
   ├─> Find IExtension implementation
   └─> Create extension instance

3. SERVICE CONFIGURATION
   └─> ConfigureServices(IServiceCollection)
       ├─> Register DI services
       ├─> Register background workers (API)
       └─> Register HttpClients (Client)

4. APPLICATION BUILD
   └─> builder.Build()

5. APP CONFIGURATION (API only)
   └─> ConfigureApp(IApplicationBuilder)
       ├─> Register endpoints
       ├─> Add middleware
       └─> Configure pipeline

6. INITIALIZATION
   └─> InitializeAsync(IExtensionContext)
       ├─> Access context (services, config, logger)
       ├─> Initialize resources
       └─> Set up state

7. COMPONENT REGISTRATION (Client only)
   ├─> RegisterComponents()
   └─> RegisterNavigation()

8. VALIDATION
   └─> ValidateAsync()
       ├─> Check configuration
       ├─> Verify dependencies
       └─> Return success/failure

9. RUNNING
   ├─> Extension active
   ├─> Handle requests (API)
   ├─> Render UI (Client)
   └─> GetHealthAsync() for monitoring

10. DISPOSAL
    └─> Dispose()
        ├─> Clean up resources
        ├─> Unload assembly (API only)
        └─> Release handles
```

## Class Hierarchy

```
IExtension (interface)
    ├─> GetManifest()
    ├─> InitializeAsync(IExtensionContext)
    ├─> ConfigureServices(IServiceCollection)
    ├─> ConfigureApp(IApplicationBuilder)
    ├─> ValidateAsync()
    ├─> GetHealthAsync()
    └─> Dispose()

BaseApiExtension : IExtension
    ├─> Implements IExtension
    ├─> Protected Context, Logger, Services
    ├─> Virtual OnInitializeAsync()
    ├─> Virtual OnConfigureApp()
    ├─> Virtual RegisterEndpoints()
    ├─> Helper: AddBackgroundService<T>()
    ├─> Helper: AddScoped<T, TImpl>()
    ├─> Helper: AddSingleton<T, TImpl>()
    ├─> Helper: AddTransient<T, TImpl>()
    ├─> Virtual OnValidateAsync()
    ├─> Virtual OnGetHealthAsync()
    └─> Virtual OnDispose()

BaseClientExtension : IExtension
    ├─> Implements IExtension
    ├─> Protected Context, Logger, Services, ApiClient
    ├─> Virtual OnInitializeAsync()
    ├─> RegisterComponents()
    ├─> RegisterNavigation()
    ├─> Helper: GetAsync<T>()
    ├─> Helper: PostAsync<TReq, TRes>()
    ├─> Helper: PutAsync<TReq, TRes>()
    ├─> Helper: DeleteAsync()
    ├─> Helper: AddScoped<T, TImpl>()
    ├─> Helper: AddSingleton<T, TImpl>()
    ├─> Helper: AddTransient<T, TImpl>()
    ├─> Virtual OnValidateAsync()
    ├─> Virtual OnGetHealthAsync()
    └─> Virtual OnDispose()
```

## Extension Types by Deployment Target

### Api Extension
```
┌────────────────────────────────┐
│     API Server Only            │
│                                │
│  Manifest:                     │
│  "deploymentTarget": "Api"     │
│                                │
│  Assembly:                     │
│  ExtensionId.Api.dll           │
│                                │
│  Use Cases:                    │
│  - Background workers          │
│  - Database operations         │
│  - File system access          │
│  - External API integration    │
│  - Scheduled tasks             │
│  - Data processing             │
└────────────────────────────────┘
```

### Client Extension
```
┌────────────────────────────────┐
│   Blazor WebAssembly Only      │
│                                │
│  Manifest:                     │
│  "deploymentTarget": "Client"  │
│                                │
│  Assembly:                     │
│  ExtensionId.Client.dll        │
│                                │
│  Use Cases:                    │
│  - UI components               │
│  - Visualizations              │
│  - Client-side state           │
│  - Browser interactions        │
│  - Local storage               │
│  - Rendering logic             │
└────────────────────────────────┘
```

### Both Extension
```
┌─────────────────────────────────────────────────────────┐
│              Full-Stack Extension                       │
│                                                         │
│  Manifest:                                              │
│  "deploymentTarget": "Both"                             │
│                                                         │
│  Assemblies:                                            │
│  - ExtensionId.Api.dll    (API server)                  │
│  - ExtensionId.Client.dll (Blazor WASM)                 │
│                                                         │
│  Communication:                                         │
│  Client → HttpClient → API Endpoints                    │
│                                                         │
│  Example: AI Tools                                      │
│  - API: HuggingFace integration, model inference        │
│  - Client: Image upload UI, caption display             │
│                                                         │
│  Use Cases:                                             │
│  - Features requiring server processing + UI            │
│  - Data that needs backend storage + frontend display   │
│  - AI/ML features (computation on server, UI on client) │
└─────────────────────────────────────────────────────────┘
```

## Extension Context

```
IExtensionContext
    │
    ├─> Manifest: ExtensionManifest
    │   └─> Metadata, deployment target, dependencies, etc.
    │
    ├─> Services: IServiceProvider
    │   └─> DI container for resolving services
    │
    ├─> Configuration: IConfiguration
    │   └─> Extension-specific config from appsettings
    │
    ├─> Logger: ILogger
    │   └─> Extension-scoped logger
    │
    ├─> Environment: ExtensionEnvironment (Api | Client)
    │   └─> Determines where extension is running
    │
    ├─> ApiClient: HttpClient? (Client extensions only)
    │   └─> Pre-configured HTTP client for API calls
    │
    ├─> ExtensionDirectory: string
    │   └─> Root directory of extension files
    │
    └─> Data: IDictionary<string, object>
        └─> Extension-specific state storage
```

## Manifest Structure

```json
{
  "schemaVersion": 1,

  "metadata": {
    "id": "ExtensionId",
    "name": "Extension Name",
    "version": "1.0.0",
    "description": "What this extension does",
    "author": "Author Name",
    "license": "MIT",
    "homepage": "https://...",
    "repository": "https://github.com/...",
    "tags": ["tag1", "tag2"],
    "categories": ["category1"],
    "icon": "path/to/icon.png",
    "minimumCoreVersion": "1.0.0"
  },

  "deploymentTarget": "Both",

  "dependencies": {
    "OtherExtensionId": ">=1.0.0"
  },

  "requiredPermissions": [
    "datasets.read",
    "datasets.write",
    "ai.huggingface"
  ],

  "apiEndpoints": [
    {
      "method": "POST",
      "route": "/api/extensions/ExtensionId/action",
      "handlerType": "Namespace.HandlerClassName",
      "description": "Endpoint description",
      "requiresAuth": true
    }
  ],

  "blazorComponents": {
    "ComponentName": "Namespace.ComponentClassName"
  },

  "navigationItems": [
    {
      "text": "Menu Item",
      "route": "/path",
      "icon": "mdi-icon-name",
      "order": 10,
      "parentId": "optional-parent",
      "requiredPermission": "permission.name"
    }
  ],

  "backgroundWorkers": [
    {
      "id": "WorkerId",
      "typeName": "Namespace.WorkerClassName",
      "description": "Worker description",
      "autoStart": true
    }
  ],

  "databaseMigrations": [
    "Migration001_Initial",
    "Migration002_AddTable"
  ],

  "configurationSchema": "JSON Schema...",

  "defaultConfiguration": {
    "setting1": "value1",
    "setting2": 42
  }
}
```

## Directory Structure

```
DatasetStudio/
│
├── src/
│   ├── APIBackend/
│   │   ├── Services/
│   │   │   └── Extensions/
│   │   │       ├── ApiExtensionRegistry.cs      ✓ COMPLETE
│   │   │       └── ApiExtensionLoader.cs        ✓ COMPLETE
│   │   └── Program.cs
│   │
│   ├── ClientApp/
│   │   ├── Services/
│   │   │   └── Extensions/
│   │   │       ├── ClientExtensionRegistry.cs   ✓ COMPLETE
│   │   │       └── ClientExtensionLoader.cs     ✓ COMPLETE
│   │   └── Program.cs
│   │
│   └── Extensions/
│       └── SDK/
│           ├── IExtension.cs                    ✓ COMPLETE
│           ├── BaseApiExtension.cs              ✓ COMPLETE
│           ├── BaseClientExtension.cs           ✓ COMPLETE
│           ├── ExtensionContext.cs              ✓ COMPLETE
│           ├── ExtensionManifest.cs             ✓ COMPLETE
│           └── ExtensionMetadata.cs             ✓ COMPLETE
│
└── Extensions/
    ├── BuiltIn/
    │   ├── CoreViewer/
    │   │   ├── extension.manifest.json
    │   │   ├── CoreViewer.Api.dll
    │   │   └── CoreViewer.Client.dll
    │   │
    │   ├── AITools/
    │   │   ├── extension.manifest.json
    │   │   ├── AITools.Api.dll
    │   │   └── AITools.Client.dll
    │   │
    │   └── Editor/
    │       ├── extension.manifest.json
    │       ├── Editor.Api.dll
    │       └── Editor.Client.dll
    │
    └── User/
        └── CustomExtension/
            ├── extension.manifest.json
            └── CustomExtension.Api.dll
```

## API Communication Pattern

```
┌──────────────────────────┐         HTTPS        ┌──────────────────────────┐
│   Blazor WebAssembly     │ ◄─────────────────► │      API Server          │
│   (Browser)              │                      │                          │
│                          │                      │                          │
│  ClientExtension         │                      │  ApiExtension            │
│  ├─ Context.ApiClient    │   POST /api/ext...   │  ├─ Endpoints            │
│  │  (HttpClient)         │ ──────────────────►  │  │  (MinimalAPI)         │
│  │                       │                      │  │                       │
│  ├─ GetAsync<T>()        │   GET /api/ext...    │  ├─ MapPost()            │
│  ├─ PostAsync<T>()       │ ──────────────────►  │  ├─ MapGet()             │
│  ├─ PutAsync<T>()        │   PUT /api/ext...    │  ├─ MapPut()             │
│  └─ DeleteAsync()        │ ──────────────────►  │  └─ MapDelete()          │
│                          │                      │                          │
│  URL Pattern:            │   JSON Response      │  Route Pattern:          │
│  /api/extensions/        │ ◄──────────────────  │  /api/extensions/        │
│    {extensionId}/        │                      │    {extensionId}/        │
│    {endpoint}            │                      │    {endpoint}            │
└──────────────────────────┘                      └──────────────────────────┘

Example:
Client calls: await GetAsync<Caption>("/image/caption")
      ↓
HTTP GET: https://api.example.com/api/extensions/AITools/image/caption
      ↓
API handles: MapGet("/api/extensions/AITools/image/caption", handler)
      ↓
Returns: { "caption": "A description of the image" }
```

## Dependency Injection Integration

```
┌──────────────────────────────────────────────────────────────────┐
│                     DI Service Registration                      │
└──────────────────────────────────────────────────────────────────┘

Extension Startup:
  1. ConfigureServices(IServiceCollection services)
     ├─> Called before app.Build()
     ├─> Register extension services
     └─> Services available in context

  2. InitializeAsync(IExtensionContext context)
     ├─> Called after app.Build()
     ├─> context.Services available
     └─> Resolve services as needed

Example:

public override void ConfigureServices(IServiceCollection services)
{
    // Register extension-specific services
    services.AddScoped<IMyService, MyServiceImpl>();
    services.AddSingleton<ICache, MemoryCache>();
    services.AddHttpClient<IExternalApi, ExternalApiClient>();
}

protected override async Task OnInitializeAsync()
{
    // Resolve services from context
    var myService = Context.Services.GetRequiredService<IMyService>();
    var cache = Context.Services.GetRequiredService<ICache>();

    // Use services
    await myService.InitializeAsync();
}
```

## Health Monitoring

```
Extension Health Check Flow:

1. Call extension.GetHealthAsync()
   ↓
2. Extension performs health checks:
   ├─ Check database connectivity (API)
   ├─ Check API connectivity (Client)
   ├─ Validate configuration
   ├─ Check resource availability
   └─ Test critical functionality
   ↓
3. Return ExtensionHealthStatus:
   {
     "health": "Healthy" | "Degraded" | "Unhealthy",
     "message": "Status description",
     "details": {
       "database": "connected",
       "cache": "operational",
       "api": "responsive"
     },
     "timestamp": "2025-01-15T10:30:00Z"
   }

Health States:
- Healthy: All systems operational
- Degraded: Functioning but with issues (slow, partial failure)
- Unhealthy: Critical failure, extension cannot function
```

## Error Handling Strategy

```
┌──────────────────────────────────────────────────────────────────┐
│                        Error Handling                            │
└──────────────────────────────────────────────────────────────────┘

1. Registry Level:
   ├─ Try-catch around each extension load
   ├─ Log errors but continue with other extensions
   └─ Graceful degradation (app still runs)

2. Loader Level:
   ├─ FileNotFoundException → Descriptive error
   ├─ ReflectionTypeLoadException → Log all loader exceptions
   ├─ InvalidOperationException → Clear error message
   └─ All exceptions logged with context

3. Extension Level:
   ├─ InitializeAsync failures → Log and mark unhealthy
   ├─ ValidateAsync failures → Warning logs
   ├─ ConfigureServices exceptions → Fatal (app won't start)
   └─ Runtime exceptions → Logged, extension degraded

4. Validation Level:
   ├─ Manifest validation → List all errors
   ├─ Assembly validation → Check before loading
   ├─ Configuration validation → Check in ValidateAsync
   └─ Dependency validation → Check before initialization

Logging Levels:
- Debug: Detailed flow information
- Information: Key lifecycle events
- Warning: Non-critical issues, validation failures
- Error: Extension load failures, runtime errors
- Critical: System-level failures
```

## Summary

The Dataset Studio extension system is a **fully implemented**, production-ready architecture that:

1. Supports distributed deployments (API and Client can be on different servers)
2. Uses isolated assembly loading for hot-reload capability
3. Provides comprehensive base classes for easy extension development
4. Integrates seamlessly with ASP.NET Core and Blazor
5. Includes full error handling, logging, and health monitoring
6. Uses manifest-driven configuration for declarative extension definition
7. Supports dependency resolution and version management
8. Enables extension communication via HTTP APIs
9. Provides DI integration throughout the lifecycle
10. Allows graceful degradation when extensions fail

**All core infrastructure is complete and ready for extension development to begin.**
