# Dataset Studio Extension System

## Quick Links

- **[Development Guide](SDK/DEVELOPMENT_GUIDE.md)** - Complete guide to creating extensions
- **[Implementation Summary](PHASE3_IMPLEMENTATION_SUMMARY.md)** - Technical overview of the system
- **[Configuration Examples](SDK/APPSETTINGS_EXAMPLES.md)** - How to configure extensions
- **[Program.cs Integration](SDK/PROGRAM_INTEGRATION.md)** - How to integrate into your app
- **[Extension Scaffolds](BuiltIn/EXTENSION_SCAFFOLDS.md)** - Reference implementations (if created)

## What is the Extension System?

The Dataset Studio Extension System is a **distributed plugin architecture** designed for scenarios where the API backend and Blazor WebAssembly client run on **different servers**.

### Core Concept

Extensions are split into three parts:

```
MyExtension/
├── MyExtension.Api       → Runs on server (REST APIs, database, AI processing)
├── MyExtension.Client    → Runs in browser (Blazor UI, user interactions)
└── MyExtension.Shared    → DTOs used by both (type-safe communication)
```

The Client calls the API via HTTP REST endpoints with type-safe DTOs.

## Architecture Diagram

```
┌──────────────────────────────────────────────────────────────┐
│                    Extension System                           │
├──────────────────────────────────────────────────────────────┤
│                                                                │
│  ┌──────────────────┐  HTTP REST   ┌──────────────────┐     │
│  │   API Server     │ ◄──────────► │  Client (Browser) │     │
│  │  (ASP.NET Core)  │              │  (Blazor WASM)    │     │
│  └──────────────────┘              └──────────────────┘     │
│          │                                  │                 │
│          │ Loads                            │ Loads           │
│          ▼                                  ▼                 │
│  ┌──────────────────┐              ┌──────────────────┐     │
│  │  *.Api.dll       │              │  *.Client.dll     │     │
│  │  Extensions      │              │  Extensions       │     │
│  └──────────────────┘              └──────────────────┘     │
│                                                                │
│  Examples:                         Examples:                 │
│  • CoreViewer.Api                  • CoreViewer.Client       │
│  • AITools.Api                     • AITools.Client          │
│    - HuggingFace calls               - UI for captioning     │
│    - Background workers              - Progress indicators   │
│  • Editor.Api                      • Editor.Client           │
│    - Batch operations                - Rich text editor      │
│                                                                │
└──────────────────────────────────────────────────────────────┘
```

## Deployment Scenarios

### 1. Local Development

Both on same machine:
```
http://localhost:5001
├── API Server
└── Client (served from wwwroot)
```

### 2. Distributed Production

Separate servers:
```
https://api.myapp.com       → API Server + Extensions
https://app.myapp.com       → Client + Extensions (CDN)
```

### 3. Cloud Deployment

```
Azure/AWS Container         → API
Azure CDN / CloudFront      → Client (globally distributed)
```

## Getting Started

### For Extension Developers

**Step 1:** Read the [Development Guide](SDK/DEVELOPMENT_GUIDE.md)

**Step 2:** Choose a deployment target:
- **API only**: Server-side processing, no UI
- **Client only**: UI components, calls existing APIs
- **Both**: Full-stack feature (most common)

**Step 3:** Create your extension:

```bash
mkdir -p Extensions/BuiltIn/MyExtension
cd Extensions/BuiltIn/MyExtension

# Create manifest
cat > extension.manifest.json <<EOF
{
  "schemaVersion": 1,
  "metadata": {
    "id": "MyExtension",
    "name": "My Extension",
    "version": "1.0.0"
  },
  "deploymentTarget": "Both"
}
EOF

# Create API extension
mkdir MyExtension.Api
# Create Client extension
mkdir MyExtension.Client
# Create Shared models
mkdir MyExtension.Shared
```

**Step 4:** Implement the extension classes:

```csharp
// MyExtension.Api/MyExtensionApiExtension.cs
public class MyExtensionApiExtension : BaseApiExtension, IExtensionApiEndpoint
{
    public override ExtensionManifest GetManifest() { ... }
    public void RegisterEndpoints(IEndpointRouteBuilder endpoints) { ... }
}

// MyExtension.Client/MyExtensionClientExtension.cs
public class MyExtensionClientExtension : BaseClientExtension
{
    public override ExtensionManifest GetManifest() { ... }
    public override void RegisterComponents() { ... }
}
```

**Step 5:** Test and deploy!

See [Development Guide](SDK/DEVELOPMENT_GUIDE.md) for complete details.

### For Application Integrators

**Step 1:** Configure appsettings.json

See [Configuration Examples](SDK/APPSETTINGS_EXAMPLES.md)

**Step 2:** Integrate into Program.cs

See [Program.cs Integration](SDK/PROGRAM_INTEGRATION.md)

**Step 3:** Deploy extensions

Copy extension folders to `Extensions/BuiltIn/` or `Extensions/User/`

## Built-In Extensions

Dataset Studio includes four built-in extensions:

### 1. CoreViewer
**Purpose:** Basic dataset viewing (grid, list, detail views)

**Deployment Target:** Both

**Features:**
- Grid view with pagination
- List view with filtering
- Detail view for individual items
- Dataset statistics

### 2. Creator
**Purpose:** Create and import datasets

**Deployment Target:** Both

**Features:**
- Create new datasets
- Import from files (JSON, CSV, Parquet)
- Import from HuggingFace Hub
- Batch upload

### 3. Editor
**Purpose:** Edit dataset items and metadata

**Deployment Target:** Both

**Features:**
- Edit individual items
- Batch editing
- Delete items
- Undo/redo support

### 4. AITools
**Purpose:** AI/ML integration (HuggingFace, etc.)

**Deployment Target:** Both

**Features:**
- Image captioning
- Auto-tagging
- Batch AI processing
- Background job queue

## Directory Structure

```
Extensions/
├── SDK/                                    → Extension SDK (base classes)
│   ├── IExtension.cs                       → Base interface
│   ├── BaseApiExtension.cs                 → API base class
│   ├── BaseClientExtension.cs              → Client base class
│   ├── ExtensionContext.cs                 → Shared context
│   ├── ExtensionManifest.cs                → Manifest management
│   ├── ExtensionApiClient.cs               → HTTP client for API calls
│   ├── IExtensionApiEndpoint.cs            → API endpoint contract
│   ├── DEVELOPMENT_GUIDE.md                → Complete dev guide
│   ├── APPSETTINGS_EXAMPLES.md             → Configuration examples
│   └── PROGRAM_INTEGRATION.md              → Integration guide
│
├── BuiltIn/                                → Built-in extensions
│   ├── CoreViewer/
│   │   ├── extension.manifest.json
│   │   ├── CoreViewer.Api/
│   │   ├── CoreViewer.Client/
│   │   └── CoreViewer.Shared/
│   ├── Creator/
│   ├── Editor/
│   └── AITools/
│
├── UserExtensions/                         → User-installed extensions
│   └── (user extensions go here)
│
├── PHASE3_IMPLEMENTATION_SUMMARY.md        → Technical summary
└── README.md                               → This file
```

## API Integration

In your API Backend's Program.cs:

```csharp
using DatasetStudio.APIBackend.Services.Extensions;

// BEFORE builder.Build()
var extensionRegistry = new ApiExtensionRegistry(builder.Configuration, builder.Services);
await extensionRegistry.DiscoverAndLoadAsync();

var app = builder.Build();

// AFTER app.Build()
await extensionRegistry.ConfigureExtensionsAsync(app);
```

## Client Integration

In your Blazor WASM Client's Program.cs:

```csharp
using DatasetStudio.ClientApp.Services.Extensions;

// BEFORE builder.Build()
var extensionRegistry = new ClientExtensionRegistry(builder.Configuration, builder.Services);
await extensionRegistry.DiscoverAndLoadAsync();

var host = builder.Build();

// AFTER host.Build()
await extensionRegistry.ConfigureExtensionsAsync();
```

## Configuration

### API (appsettings.json)

```json
{
  "Extensions": {
    "Enabled": true,
    "Directory": "./Extensions/BuiltIn",
    "UserDirectory": "./Extensions/User"
  },
  "Extensions:AITools": {
    "HuggingFaceApiKey": "your-key-here"
  }
}
```

### Client (appsettings.json)

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

## Key Features

### ✅ Distributed Architecture
- API and Client can be on different servers
- Scales independently
- CDN-friendly client distribution

### ✅ Type-Safe Communication
- Shared DTOs between API and Client
- Compile-time safety
- No magic strings

### ✅ Dynamic Loading
- Extensions discovered at runtime
- No recompilation needed
- AssemblyLoadContext for isolation

### ✅ Manifest-Driven
- Single source of truth (extension.manifest.json)
- Declarative configuration
- Automatic registration

### ✅ Lifecycle Management
- Dependency resolution
- Ordered initialization
- Health monitoring
- Graceful shutdown

### ✅ Security
- Permission system
- Configuration validation
- Isolated execution

## Extension Manifest Example

```json
{
  "schemaVersion": 1,
  "metadata": {
    "id": "MyExtension",
    "name": "My Extension",
    "version": "1.0.0",
    "description": "What my extension does",
    "author": "Your Name"
  },
  "deploymentTarget": "Both",
  "requiredPermissions": [
    "datasets.read",
    "datasets.write"
  ],
  "apiEndpoints": [
    {
      "method": "POST",
      "route": "/api/extensions/myext/process",
      "description": "Process data"
    }
  ],
  "navigationItems": [
    {
      "text": "My Extension",
      "route": "/myextension",
      "icon": "mdi-star"
    }
  ]
}
```

## Communication Example

### Client calls API:

**Client Extension:**
```csharp
public class MyClientExtension : BaseClientExtension
{
    public async Task<Result> ProcessAsync(string data)
    {
        var request = new ProcessRequest { Data = data };
        return await PostAsync<ProcessRequest, Result>("/process", request);
    }
}
```

**API Extension:**
```csharp
public class MyApiExtension : BaseApiExtension, IExtensionApiEndpoint
{
    public void RegisterEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/extensions/myext/process",
            async (ProcessRequest req) =>
            {
                // Process server-side
                return Results.Ok(new Result { Success = true });
            });
    }
}
```

**Shared Models:**
```csharp
// MyExtension.Shared/Models.cs
public class ProcessRequest
{
    public required string Data { get; set; }
}

public class Result
{
    public required bool Success { get; set; }
}
```

## Testing

### Unit Testing
```csharp
[Fact]
public async Task Extension_Initializes_Successfully()
{
    var extension = new MyExtension();
    var context = CreateMockContext();

    await extension.InitializeAsync(context);

    Assert.True(await extension.ValidateAsync());
}
```

### Integration Testing
```csharp
[Fact]
public async Task ApiEndpoint_Returns_ExpectedResult()
{
    var client = _factory.CreateClient();

    var response = await client.PostAsJsonAsync(
        "/api/extensions/myext/process",
        new ProcessRequest { Data = "test" });

    response.EnsureSuccessStatusCode();
}
```

## Support

- **Documentation:** [Development Guide](SDK/DEVELOPMENT_GUIDE.md)
- **Examples:** See `BuiltIn/` directory for reference implementations
- **Issues:** GitHub Issues
- **Community:** Discord / Forums

## License

See LICENSE file in root directory.

---

**Ready to build your first extension?** Start with the [Development Guide](SDK/DEVELOPMENT_GUIDE.md)!
