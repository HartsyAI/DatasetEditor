# Extension System - Complete Implementation Plan

## Executive Summary

This document provides a comprehensive plan for implementing the Dataset Studio extension system, answering critical architectural decisions and providing a step-by-step implementation guide.

---

## Critical Decision: Extension Project Structure

### The Question: Full .csproj Projects vs Simple Classes?

**ANSWER: Full .csproj Projects as Git Submodules**

Here's why and how:

---

## Extension Packaging Model

### Full .csproj Projects as Git Repositories (FINAL DECISION ✅)

```
MyExtension/                        # Separate GitHub repo
├── MyExtension.sln
├── src/
│   ├── MyExtension.Api/
│   │   ├── MyExtension.Api.csproj
│   │   ├── MyExtensionApiExtension.cs
│   │   ├── Services/
│   │   ├── Endpoints/
│   │   └── Models/
│   │
│   ├── MyExtension.Client/
│   │   ├── MyExtension.Client.csproj
│   │   ├── MyExtensionClientExtension.cs
│   │   ├── Components/
│   │   ├── Pages/
│   │   └── Services/
│   │
│   └── MyExtension.Shared/
│       ├── MyExtension.Shared.csproj
│       ├── DTOs/
│       └── Models/
│
├── extension.manifest.json
├── README.md
├── .gitignore
└── LICENSE
```

**Distribution Model:**
- Each extension is a **separate GitHub repository**
- Extensions are **cloned** into Dataset Studio's Extensions folder
- Extensions can have their **own NuGet dependencies** (e.g., Newtonsoft.Json, ML.NET)
- Built DLLs are **dynamically loaded** at runtime

**Advantages:**
1. ✅ **Separate GitHub Repos** - Each extension is completely independent
2. ✅ **Simple Installation** - `git clone` into Extensions folder
3. ✅ **Dependency Management** - Extensions can use any NuGet packages they need
4. ✅ **Independent Development** - Extensions developed separately from core
5. ✅ **Community Contributions** - Third-party developers create their own repos
6. ✅ **Version Control** - Full git history per extension
7. ✅ **Easy Updates** - `git pull` to update extension
8. ✅ **No Complex Packaging** - No need to publish NuGet packages

**Dataset Studio Directory Structure:**
```
DatasetStudio/                      # Main repo
├── src/
│   ├── APIBackend/
│   ├── ClientApp/
│   ├── Core/
│   ├── DTO/
│   └── Extensions/
│       └── SDK/                    # SDK project (part of main repo)
│
├── Extensions/
│   ├── BuiltIn/                    # Built-in extensions (git submodules)
│   │   ├── CoreViewer/             # git submodule → github.com/hartsy/ds-ext-coreviewer
│   │   ├── Creator/                # git submodule → github.com/hartsy/ds-ext-creator
│   │   └── Editor/                 # git submodule → github.com/hartsy/ds-ext-editor
│   │
│   └── Community/                  # Third-party extensions (git clone)
│       ├── MyCustomExtension/      # git clone → github.com/user/my-extension
│       └── AnotherExtension/       # git clone → github.com/other/another-ext
│
└── ApprovedExtensions.json         # Curated list of approved extensions
```

---

## Extension Distribution Model

### Two Distribution Channels

#### 1. Built-In Extensions (Official, Shipped with Dataset Studio)

**Location:** `Extensions/BuiltIn/`

**Technology:** Git Submodules

**Examples:**
- CoreViewer
- Creator
- Editor
- AITools

**Setup:**
```bash
# Add built-in extension as git submodule
git submodule add https://github.com/hartsy/ds-ext-coreviewer.git Extensions/BuiltIn/CoreViewer
git submodule add https://github.com/hartsy/ds-ext-creator.git Extensions/BuiltIn/Creator
git submodule add https://github.com/hartsy/ds-ext-editor.git Extensions/BuiltIn/Editor

# Clone with submodules
git clone --recursive https://github.com/hartsy/dataset-studio.git

# Or initialize submodules after clone
git submodule update --init --recursive
```

**Build Process:**
```bash
# Build main solution (includes all extensions)
dotnet build DatasetStudio.sln

# Extensions build their own DLLs in place:
# Extensions/BuiltIn/CoreViewer/src/CoreViewer.Api/bin/Release/net8.0/CoreViewer.Api.dll
# Extensions/BuiltIn/CoreViewer/src/CoreViewer.Client/bin/Release/net8.0/CoreViewer.Client.dll
```

**Updating Built-In Extensions:**
```bash
# Update all built-in extensions
git submodule update --remote --merge

# Update specific extension
cd Extensions/BuiltIn/CoreViewer
git pull origin main
cd ../../..
git add Extensions/BuiltIn/CoreViewer
git commit -m "Update CoreViewer extension"
```

#### 2. Community Extensions (Third-Party)

**Location:** `Extensions/Community/`

**Technology:** Git Clone (manual)

**Installation Methods:**

**Method 1: Manual Git Clone**
```bash
cd Extensions/Community
git clone https://github.com/someuser/awesome-extension.git AwesomeExtension
cd AwesomeExtension
dotnet build -c Release
```

**Method 2: Admin UI Installation**
```
Admin Panel → Extensions → Install from GitHub
  ↓
Enter GitHub URL: https://github.com/someuser/awesome-extension
  ↓
Dataset Studio:
  1. Clones repo to Extensions/Community/AwesomeExtension/
  2. Runs dotnet restore
  3. Runs dotnet build -c Release
  4. Validates extension.manifest.json
  5. Loads extension
```

**Updating Community Extensions:**
```bash
# Via git
cd Extensions/Community/AwesomeExtension
git pull origin main
dotnet build -c Release

# Or via Admin UI
Admin Panel → Extensions → AwesomeExtension → Check for Updates
```

---

## Approved Extensions Registry

### ApprovedExtensions.json

**Location:** Root of Dataset Studio repository

**Purpose:** Curated list of verified, safe, community extensions

**Format:**
```json
{
  "schemaVersion": 1,
  "lastUpdated": "2025-01-15T10:00:00Z",
  "extensions": [
    {
      "id": "CoreViewer",
      "name": "Core Viewer",
      "author": "Hartsy",
      "description": "Basic dataset viewing with grid, list, and masonry layouts",
      "repositoryUrl": "https://github.com/hartsy/ds-ext-coreviewer",
      "category": "BuiltIn",
      "verified": true,
      "minCoreVersion": "1.0.0",
      "latestVersion": "1.2.0",
      "downloadCount": 0,
      "rating": 5.0,
      "tags": ["viewer", "grid", "list", "official"]
    },
    {
      "id": "AwesomeExtension",
      "name": "Awesome Dataset Tools",
      "author": "CommunityDev",
      "description": "Advanced dataset manipulation and analysis tools",
      "repositoryUrl": "https://github.com/communitydev/awesome-ds-extension",
      "category": "Community",
      "verified": true,
      "minCoreVersion": "1.0.0",
      "latestVersion": "2.1.0",
      "downloadCount": 1250,
      "rating": 4.7,
      "tags": ["tools", "analysis", "community"]
    }
  ]
}
```

**Usage in Admin UI:**
```csharp
public class ExtensionBrowserService
{
    public async Task<List<ApprovedExtension>> GetApprovedExtensionsAsync()
    {
        // Fetch from GitHub
        var url = "https://raw.githubusercontent.com/hartsy/dataset-studio/main/ApprovedExtensions.json";
        var json = await _httpClient.GetStringAsync(url);
        var registry = JsonSerializer.Deserialize<ApprovedExtensionRegistry>(json);

        return registry.Extensions;
    }

    public async Task InstallExtensionAsync(string extensionId)
    {
        var extension = await GetApprovedExtensionByIdAsync(extensionId);

        // Clone from GitHub
        await GitCloneAsync(extension.RepositoryUrl, $"Extensions/Community/{extensionId}");

        // Build extension
        await DotnetBuildAsync($"Extensions/Community/{extensionId}");

        // Validate and load
        await LoadExtensionAsync(extensionId);
    }
}
```

**Admin UI Flow:**
```
Admin Panel → Extensions → Browse Approved Extensions
  ↓
Display list from ApprovedExtensions.json
  - Show name, description, rating, download count
  - Filter by category, tags
  - Search by name
  ↓
User clicks "Install"
  ↓
Extension cloned from GitHub → Built → Loaded
```

**Verification Process:**
1. Developer submits extension via GitHub issue/PR
2. Dataset Studio team reviews code, security, functionality
3. If approved, added to ApprovedExtensions.json
4. Marked as `"verified": true`
5. Users can install with confidence

---

## Permission System Integration

### Extension Permissions Model

Extensions declare required permissions in their manifest and are restricted by user roles.

#### Manifest Permission Declaration

```json
{
  "schemaVersion": 1,
  "metadata": {
    "id": "Editor",
    "name": "Advanced Editor"
  },
  "requiredPermissions": [
    "datasets.read",
    "datasets.write",
    "datasets.delete",
    "items.bulk_edit",
    "filesystem.read"
  ]
}
```

#### User Role → Permission Mapping

**Database Schema:**
```sql
-- User roles
CREATE TABLE Roles (
    Id UUID PRIMARY KEY,
    Name TEXT NOT NULL,
    Description TEXT,
    IsSystemRole BOOLEAN DEFAULT FALSE
);

-- System roles
INSERT INTO Roles (Id, Name, Description, IsSystemRole) VALUES
('admin-role', 'Administrator', 'Full access to all features', TRUE),
('editor-role', 'Editor', 'Can edit datasets but not manage users', TRUE),
('viewer-role', 'Viewer', 'Can only view datasets', TRUE),
('restricted-role', 'Restricted', 'Limited access', TRUE);

-- Permissions
CREATE TABLE Permissions (
    Id UUID PRIMARY KEY,
    Name TEXT UNIQUE NOT NULL,
    Description TEXT,
    Category TEXT
);

-- Extension permissions
INSERT INTO Permissions (Id, Name, Description, Category) VALUES
('perm-datasets-read', 'datasets.read', 'Read datasets', 'Datasets'),
('perm-datasets-write', 'datasets.write', 'Create/update datasets', 'Datasets'),
('perm-datasets-delete', 'datasets.delete', 'Delete datasets', 'Datasets'),
('perm-items-bulk-edit', 'items.bulk_edit', 'Bulk edit items', 'Items'),
('perm-filesystem-read', 'filesystem.read', 'Read filesystem', 'System'),
('perm-extensions-manage', 'extensions.manage', 'Install/uninstall extensions', 'Extensions'),
('perm-users-manage', 'users.manage', 'Manage users', 'Admin');

-- Role permissions
CREATE TABLE RolePermissions (
    RoleId UUID REFERENCES Roles(Id),
    PermissionId UUID REFERENCES Permissions(Id),
    PRIMARY KEY (RoleId, PermissionId)
);

-- Administrator: All permissions
INSERT INTO RolePermissions (RoleId, PermissionId)
SELECT 'admin-role', Id FROM Permissions;

-- Editor: Can read/write datasets, bulk edit
INSERT INTO RolePermissions (RoleId, PermissionId)
SELECT 'editor-role', Id FROM Permissions
WHERE Name IN ('datasets.read', 'datasets.write', 'items.bulk_edit');

-- Viewer: Can only read
INSERT INTO RolePermissions (RoleId, PermissionId)
SELECT 'viewer-role', Id FROM Permissions
WHERE Name = 'datasets.read';

-- User extension permissions
CREATE TABLE UserExtensionPermissions (
    UserId UUID REFERENCES Users(Id),
    ExtensionId TEXT NOT NULL,
    IsEnabled BOOLEAN DEFAULT TRUE,
    GrantedPermissions JSONB,  -- Override permissions per user
    CreatedAt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (UserId, ExtensionId)
);
```

#### Permission Enforcement

**Extension Loading with Permission Check:**
```csharp
public class ExtensionPermissionService
{
    private readonly IUserContext _userContext;
    private readonly IPermissionRepository _permissionRepo;

    public async Task<bool> CanUserUseExtensionAsync(Guid userId, string extensionId)
    {
        // Get extension manifest
        var manifest = await _extensionRegistry.GetManifestAsync(extensionId);

        // Get user's role
        var user = await _userContext.GetUserAsync(userId);

        // Get user's permissions
        var userPermissions = await _permissionRepo.GetUserPermissionsAsync(userId);

        // Check if user has all required permissions
        foreach (var requiredPerm in manifest.RequiredPermissions)
        {
            if (!userPermissions.Contains(requiredPerm))
            {
                _logger.LogWarning(
                    "User {UserId} lacks permission {Permission} for extension {ExtensionId}",
                    userId, requiredPerm, extensionId);
                return false;
            }
        }

        // Check user-specific extension override
        var userExtPerm = await _permissionRepo.GetUserExtensionPermissionAsync(userId, extensionId);
        if (userExtPerm != null && !userExtPerm.IsEnabled)
        {
            return false;
        }

        return true;
    }
}
```

**UI Permission Filtering:**
```razor
@* Admin UI - Extension Browser *@
@if (await PermissionService.HasPermissionAsync(CurrentUser.Id, "extensions.manage"))
{
    <MudButton OnClick="@(() => InstallExtension(extension))">Install</MudButton>
}
else
{
    <MudChip>Requires Admin Permission</MudChip>
}

@* Extension Nav Menu Item *@
@foreach (var extension in LoadedExtensions)
{
    @if (await PermissionService.CanUserUseExtensionAsync(CurrentUser.Id, extension.Id))
    {
        <MudNavLink Href="@extension.Route">@extension.Name</MudNavLink>
    }
}

@* Extension Endpoint Authorization *@
app.MapPost("/api/extensions/Editor/bulk-edit", async (HttpContext context, BulkEditRequest request) =>
{
    var userId = context.User.GetUserId();

    if (!await _permissionService.HasPermissionAsync(userId, "items.bulk_edit"))
    {
        return Results.Forbid();
    }

    // Process bulk edit
    return Results.Ok();
})
.RequireAuthorization(); // Requires authenticated user
```

#### Admin Panel - Extension Permissions Management

**UI Mockup:**
```
Admin Panel → Users → John Doe → Extension Permissions

Extension            | Enabled | Custom Permissions
---------------------|---------|-----------------------------------
CoreViewer           | ✅      | [Default: datasets.read]
Creator              | ✅      | [Default: datasets.write]
Editor               | ❌      | [Disabled for this user]
AITools              | ✅      | [Custom: Allow only caption view]
CustomExtension      | ✅      | [Default]

[Save Changes]
```

**Permission Override Example:**
```json
{
  "userId": "user-123",
  "extensionId": "AITools",
  "isEnabled": true,
  "grantedPermissions": [
    "ai.caption.view",
    "ai.caption.generate"
  ],
  "deniedPermissions": [
    "ai.caption.delete",
    "ai.model.train"
  ]
}
```

#### Permission Categories

**Datasets:**
- `datasets.read` - View datasets and items
- `datasets.write` - Create and update datasets
- `datasets.delete` - Delete datasets

**Items:**
- `items.edit` - Edit individual items
- `items.bulk_edit` - Bulk edit multiple items
- `items.delete` - Delete items

**Extensions:**
- `extensions.view` - View installed extensions
- `extensions.install` - Install new extensions
- `extensions.manage` - Configure and uninstall extensions

**System:**
- `filesystem.read` - Read local files
- `filesystem.write` - Write local files
- `network.external` - Make external HTTP requests

**Admin:**
- `users.manage` - Create, update, delete users
- `roles.manage` - Create and assign roles
- `permissions.manage` - Assign permissions

---

## Modular Architecture - How Extensions Add/Remove Features

### Extension Discovery Process

```
Startup
  │
  ├─> ApiExtensionRegistry.DiscoverAsync()
  │   │
  │   ├─> Scan Extensions/BuiltIn/
  │   │   └─> Find all extension.manifest.json files
  │   │
  │   ├─> Scan Extensions/Downloaded/
  │   │   └─> Find all extension.manifest.json files
  │   │
  │   ├─> Scan Extensions/User/
  │   │   └─> Find all extension.manifest.json files
  │   │
  │   └─> Parse & Validate Manifests
  │       ├─> Check schema version
  │       ├─> Validate metadata
  │       ├─> Check deployment target
  │       └─> Resolve dependencies
  │
  ├─> Filter by DeploymentTarget (Api/Client/Both)
  │
  ├─> Topological Sort (dependency order)
  │
  └─> Load Extensions in Order
      └─> For each extension:
          ├─> Load assembly
          ├─> Instantiate IExtension
          ├─> ConfigureServices()
          ├─> ConfigureApp()
          ├─> InitializeAsync()
          └─> ValidateAsync()
```

### Enabling/Disabling Extensions

#### Option 1: Configuration File

**appsettings.json:**
```json
{
  "Extensions": {
    "Enabled": true,
    "DisabledExtensions": [
      "AITools",
      "AdvancedTools"
    ]
  }
}
```

**Loading Logic:**
```csharp
var disabledExtensions = configuration.GetSection("Extensions:DisabledExtensions")
    .Get<List<string>>() ?? new List<string>();

foreach (var manifest in discoveredManifests)
{
    if (disabledExtensions.Contains(manifest.Metadata.Id))
    {
        _logger.LogInformation("Skipping disabled extension: {ExtensionId}", manifest.Metadata.Id);
        continue;
    }

    await LoadExtensionAsync(manifest);
}
```

#### Option 2: Database-Driven (Future)

```sql
CREATE TABLE ExtensionSettings (
    ExtensionId TEXT PRIMARY KEY,
    IsEnabled BOOLEAN,
    Configuration JSONB,
    UpdatedAt TIMESTAMP
);
```

**Benefits:**
- Per-user extension settings (multi-user support)
- Enable/disable without restarting
- UI-based management

#### Option 3: File-Based Toggle

**Extensions/BuiltIn/AITools/.disabled**
- If `.disabled` file exists, skip loading
- Users can enable/disable by creating/deleting file

### Removing Extensions

#### Uninstall Process

```csharp
public async Task UninstallExtensionAsync(string extensionId)
{
    // 1. Stop extension
    var extension = _loadedExtensions[extensionId];
    await extension.DisposeAsync();

    // 2. Unload assembly (API only)
    if (extension is ApiExtension apiExt)
    {
        apiExt.AssemblyLoadContext.Unload();
    }

    // 3. Remove from registry
    _loadedExtensions.Remove(extensionId);

    // 4. Delete files
    var extensionDir = Path.Combine(_extensionDirectory, extensionId);
    if (Directory.Exists(extensionDir))
    {
        Directory.Delete(extensionDir, recursive: true);
    }

    // 5. Clean up database (if using DB-driven settings)
    await _db.ExecuteAsync("DELETE FROM ExtensionSettings WHERE ExtensionId = @ExtensionId",
        new { ExtensionId = extensionId });

    _logger.LogInformation("Extension uninstalled: {ExtensionId}", extensionId);
}
```

---

## Extension SDK - Reference Library

### SDK as NuGet Package

**Package:** `DatasetStudio.Extensions.SDK`

**Published to NuGet.org so external developers can reference it:**

```bash
dotnet add package DatasetStudio.Extensions.SDK
```

**SDK Contents:**
```
Extensions.SDK/
├── Extensions.SDK.csproj
├── IExtension.cs
├── BaseApiExtension.cs
├── BaseClientExtension.cs
├── ExtensionContext.cs
├── ExtensionManifest.cs
├── ExtensionMetadata.cs
├── IExtensionContext.cs
├── ExtensionApiClient.cs
├── IExtensionApiEndpoint.cs
└── Models/
    ├── ExtensionHealthStatus.cs
    ├── ExtensionDeploymentTarget.cs
    └── ExtensionEnvironment.cs
```

**Extensions.SDK.csproj:**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <GeneratePackageOnBuild>true</GeneratePackageOnBuild>
    <PackageId>DatasetStudio.Extensions.SDK</PackageId>
    <Version>1.0.0</Version>
    <Authors>Hartsy</Authors>
    <Description>SDK for building Dataset Studio extensions</Description>
    <PackageProjectUrl>https://github.com/hartsy-ai/dataset-studio</PackageProjectUrl>
    <RepositoryUrl>https://github.com/hartsy-ai/dataset-studio</RepositoryUrl>
    <PackageTags>dataset-studio;extension;sdk</PackageTags>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Abstractions" Version="8.0.0" />
    <PackageReference Include="Microsoft.AspNetCore.Http.Abstractions" Version="2.2.0" />
  </ItemGroup>
</Project>
```

**Why NuGet Package?**
1. ✅ External developers can easily reference SDK
2. ✅ Semantic versioning
3. ✅ Dependency management
4. ✅ Standard .NET tooling
5. ✅ Can update SDK independently from core

---

## Extension Template Project

### .NET Template for Quick Start

**Create Template:**
```bash
dotnet new install DatasetStudio.Extension.Template
dotnet new ds-extension -n MyExtension
```

**Template Structure:**
```
templates/
└── DatasetStudio.Extension/
    ├── .template.config/
    │   └── template.json
    │
    ├── MyExtension.sln
    │
    ├── src/
    │   ├── MyExtension.Api/
    │   │   ├── MyExtension.Api.csproj
    │   │   ├── MyExtensionApiExtension.cs
    │   │   └── Endpoints/
    │   │       └── ExampleEndpoint.cs
    │   │
    │   ├── MyExtension.Client/
    │   │   ├── MyExtension.Client.csproj
    │   │   ├── MyExtensionClientExtension.cs
    │   │   ├── Components/
    │   │   │   └── ExampleComponent.razor
    │   │   └── Pages/
    │   │       └── ExamplePage.razor
    │   │
    │   └── MyExtension.Shared/
    │       ├── MyExtension.Shared.csproj
    │       └── Models/
    │           └── ExampleModel.cs
    │
    ├── extension.manifest.json
    ├── README.md
    ├── .gitignore
    └── LICENSE
```

**template.json:**
```json
{
  "$schema": "http://json.schemastore.org/template",
  "author": "Dataset Studio Team",
  "classifications": [ "Dataset Studio", "Extension" ],
  "identity": "DatasetStudio.Extension.Template",
  "name": "Dataset Studio Extension",
  "shortName": "ds-extension",
  "tags": {
    "language": "C#",
    "type": "project"
  },
  "sourceName": "MyExtension",
  "preferNameDirectory": true
}
```

---

## Extension Dependency Management

### Dependency Resolution

**Manifest Dependencies:**
```json
{
  "dependencies": {
    "CoreViewer": ">=1.0.0",
    "AITools": "^2.0.0"
  }
}
```

**Dependency Resolution Algorithm:**
```csharp
public async Task<List<ExtensionManifest>> ResolveDependenciesAsync(
    List<ExtensionManifest> manifests)
{
    // 1. Build dependency graph
    var graph = new Dictionary<string, List<string>>();
    foreach (var manifest in manifests)
    {
        graph[manifest.Metadata.Id] = manifest.Dependencies.Keys.ToList();
    }

    // 2. Topological sort (Kahn's algorithm)
    var sorted = new List<string>();
    var inDegree = new Dictionary<string, int>();

    foreach (var node in graph.Keys)
    {
        inDegree[node] = 0;
    }

    foreach (var deps in graph.Values)
    {
        foreach (var dep in deps)
        {
            if (inDegree.ContainsKey(dep))
            {
                inDegree[dep]++;
            }
        }
    }

    var queue = new Queue<string>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));

    while (queue.Count > 0)
    {
        var node = queue.Dequeue();
        sorted.Add(node);

        foreach (var dep in graph[node])
        {
            inDegree[dep]--;
            if (inDegree[dep] == 0)
            {
                queue.Enqueue(dep);
            }
        }
    }

    // 3. Check for circular dependencies
    if (sorted.Count != graph.Count)
    {
        throw new InvalidOperationException("Circular dependency detected in extensions");
    }

    // 4. Return manifests in load order
    return sorted.Select(id => manifests.First(m => m.Metadata.Id == id)).ToList();
}
```

### Version Compatibility

**Semantic Versioning Support:**
```csharp
public bool IsVersionCompatible(string required, string actual)
{
    // Parse version requirements
    // ^1.0.0 = >=1.0.0 <2.0.0 (caret)
    // ~1.0.0 = >=1.0.0 <1.1.0 (tilde)
    // >=1.0.0 = exact operator

    var versionRange = VersionRange.Parse(required);
    var version = NuGetVersion.Parse(actual);

    return versionRange.Satisfies(version);
}
```

---

## Extension Communication Patterns

### 1. API ↔ Client Communication

**Client calls API extension endpoint:**
```csharp
// Client Extension
public class MyExtensionClientExtension : BaseClientExtension
{
    public async Task<string> GetDataAsync()
    {
        // Calls: https://api.example.com/api/extensions/MyExtension/data
        var response = await GetAsync<DataResponse>("/data");
        return response.Message;
    }
}

// API Extension
public class MyExtensionApiExtension : BaseApiExtension
{
    protected override void OnConfigureApp(IApplicationBuilder app)
    {
        if (app is IEndpointRouteBuilder endpoints)
        {
            // Route: /api/extensions/MyExtension/data
            endpoints.MapGet("/api/extensions/MyExtension/data", () =>
            {
                return Results.Ok(new DataResponse { Message = "Hello from API" });
            });
        }
    }
}
```

### 2. Extension ↔ Extension Communication

**Option A: Shared Service via DI**
```csharp
// CoreViewer provides IDatasetService
public class CoreViewerApiExtension : BaseApiExtension
{
    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IDatasetService, DatasetService>();
    }
}

// Editor extension depends on CoreViewer
public class EditorApiExtension : BaseApiExtension
{
    protected override async Task OnInitializeAsync()
    {
        // Resolve service provided by CoreViewer
        var datasetService = Context.Services.GetRequiredService<IDatasetService>();
        await datasetService.InitializeAsync();
    }
}
```

**Option B: Extension API Contract**
```csharp
// CoreViewer exposes public API
public interface ICoreViewerApi
{
    Task<DatasetDto> GetDatasetAsync(Guid id);
    Task<List<DatasetItemDto>> GetItemsAsync(Guid datasetId);
}

// Register in DI
services.AddScoped<ICoreViewerApi, CoreViewerApiService>();

// Editor extension uses interface
var coreViewerApi = Context.Services.GetRequiredService<ICoreViewerApi>();
var dataset = await coreViewerApi.GetDatasetAsync(datasetId);
```

**Option C: Event Bus**
```csharp
// Extension publishes event
await Context.PublishEventAsync(new DatasetCreatedEvent
{
    DatasetId = datasetId,
    Name = "New Dataset"
});

// Other extensions subscribe
public override void ConfigureServices(IServiceCollection services)
{
    services.AddSingleton<IEventHandler<DatasetCreatedEvent>, MyEventHandler>();
}
```

### 3. Client ↔ Core Communication

**Extensions can access Dataset Studio core services:**
```csharp
protected override async Task OnInitializeAsync()
{
    // Access core repository
    var datasetRepo = Context.Services.GetRequiredService<IDatasetRepository>();
    var datasets = await datasetRepo.GetAllAsync();

    // Access core services
    var ingestionService = Context.Services.GetRequiredService<IDatasetIngestionService>();
}
```

---

## Implementation Phases

### Phase 1: Core Extension Infrastructure ✅ (COMPLETE)

**Status:** Already implemented according to EXTENSION_ARCHITECTURE.md

- ✅ IExtension interface
- ✅ BaseApiExtension
- ✅ BaseClientExtension
- ✅ ExtensionManifest
- ✅ ExtensionContext
- ✅ ApiExtensionRegistry
- ✅ ClientExtensionRegistry
- ✅ ApiExtensionLoader
- ✅ ClientExtensionLoader

### Phase 2: Extension Loading & Discovery (THIS PHASE)

**Goal:** Make the extension system operational

**Tasks:**
1. Implement manifest discovery logic
2. Implement dependency resolution
3. Implement version checking
4. Wire up extension loading in Program.cs
5. Test with a simple extension

**Files to Modify:**
- `src/APIBackend/Program.cs` - Add extension loading
- `src/ClientApp/Program.cs` - Add extension loading
- `src/APIBackend/Services/Extensions/ApiExtensionRegistry.cs` - Implement discovery
- `src/ClientApp/Services/Extensions/ClientExtensionRegistry.cs` - Implement discovery

**Estimated Time:** 4-6 hours

### Phase 3: Built-In Extension Migration

**Goal:** Convert existing features to extensions

**Tasks:**
1. Create CoreViewer extension (move existing viewer code)
2. Create Creator extension (move upload/import code)
3. Create Editor extension (NEW - build advanced editor)

**Estimated Time:** 8-12 hours per extension

### Phase 4: Extension Management UI

**Goal:** Admin panel for managing extensions

**Tasks:**
1. Create Extensions admin page
2. List installed extensions
3. Enable/disable extensions
4. Browse available extensions (NuGet)
5. Install/uninstall extensions

**Estimated Time:** 6-8 hours

### Phase 5: SDK Publication

**Goal:** Publish SDK to NuGet.org

**Tasks:**
1. Create Extensions.SDK.csproj package config
2. Add package metadata
3. Test packaging locally
4. Publish to NuGet.org
5. Create documentation

**Estimated Time:** 2-3 hours

### Phase 6: Extension Templates

**Goal:** .NET templates for easy extension creation

**Tasks:**
1. Create template project structure
2. Create template.json
3. Test template locally
4. Publish template to NuGet
5. Create "Create Your First Extension" guide

**Estimated Time:** 3-4 hours

---

## Development Workflow

### For Core Dataset Studio Developers

```bash
# 1. Work on main solution
cd DatasetStudio
dotnet build

# 2. Built-in extensions are built automatically
# Output: Extensions/BuiltIn/{ExtensionId}/

# 3. Run application
dotnet run --project src/APIBackend
```

### For Extension Developers (External)

```bash
# 1. Install .NET template
dotnet new install DatasetStudio.Extension.Template

# 2. Create new extension
dotnet new ds-extension -n MyAwesomeExtension
cd MyAwesomeExtension

# 3. Add SDK reference (automatically included in template)
dotnet add package DatasetStudio.Extensions.SDK

# 4. Develop extension
# ... write code ...

# 5. Build extension
dotnet build -c Release

# 6. Test locally
cp -r src/MyAwesomeExtension.Api/bin/Release/net8.0/* \
    /path/to/DatasetStudio/Extensions/User/MyAwesomeExtension/
cp extension.manifest.json \
    /path/to/DatasetStudio/Extensions/User/MyAwesomeExtension/

# 7. Publish to NuGet (optional)
dotnet pack -c Release
dotnet nuget push src/MyAwesomeExtension.Api/bin/Release/MyAwesomeExtension.Api.1.0.0.nupkg \
    --api-key YOUR_KEY --source https://api.nuget.org/v3/index.json
```

---

## Extension Ecosystem Vision

### Official Extensions (by Dataset Studio team)

**Published under `DatasetStudio.Extensions.*` namespace:**

1. **DatasetStudio.Extensions.CoreViewer** - Basic viewing
2. **DatasetStudio.Extensions.Creator** - Dataset creation
3. **DatasetStudio.Extensions.Editor** - Advanced editing
4. **DatasetStudio.Extensions.AITools** - AI caption generation
5. **DatasetStudio.Extensions.AdvancedTools** - Data processing
6. **DatasetStudio.Extensions.Analytics** - Usage analytics

### Community Extensions

**Published by third parties:**

1. **CommunityDev.DatasetStudio.CustomVisualization** - Custom viz
2. **ThirdParty.DatasetStudio.S3Integration** - AWS S3 support
3. **ML.DatasetStudio.AutoAnnotation** - Auto-annotation tools

### Extension Marketplace (Future)

**Web-based marketplace for discovering extensions:**
- Browse by category
- Search by functionality
- View ratings and reviews
- One-click install
- Automatic updates

---

## Security Considerations

### 1. Sandboxing

**AssemblyLoadContext Isolation:**
```csharp
var loadContext = new AssemblyLoadContext(
    name: $"Extension_{extensionId}",
    isCollectible: true);

// Extension runs in isolated context
// Can be unloaded without restarting app
```

### 2. Permissions System

**Manifest Declares Required Permissions:**
```json
{
  "requiredPermissions": [
    "datasets.read",
    "datasets.write",
    "filesystem.read",
    "network.external"
  ]
}
```

**User Must Approve:**
```
⚠️ MyExtension requires the following permissions:
- Read datasets
- Write datasets
- Access file system
- Make external network requests

[Approve] [Deny]
```

### 3. Code Signing (Future)

**Verify extension integrity:**
```csharp
public bool VerifyExtensionSignature(string dllPath)
{
    // Check Authenticode signature
    // Verify publisher certificate
    // Ensure code hasn't been tampered with
}
```

---

## Monitoring & Observability

### 1. Extension Health Dashboard

**Admin UI shows extension status:**
```
Extension Name    | Status   | Health    | Version | Loaded
------------------|----------|-----------|---------|-------
CoreViewer        | Enabled  | Healthy   | 1.0.0   | ✅
Editor            | Enabled  | Healthy   | 1.2.0   | ✅
AITools           | Disabled | N/A       | 2.0.1   | ❌
CustomExtension   | Enabled  | Degraded  | 0.5.0   | ✅
```

### 2. Extension Logs

**Separate log files per extension:**
```
Logs/
├── app.log
├── extensions/
│   ├── CoreViewer.log
│   ├── Editor.log
│   └── AITools.log
```

### 3. Telemetry

**Track extension usage:**
```csharp
Context.Telemetry.TrackEvent("FeatureUsed", new Dictionary<string, string>
{
    ["ExtensionId"] = "AITools",
    ["Feature"] = "CaptionGeneration",
    ["Model"] = "BLIP-2"
});
```

---

## Summary: Git-Based Extension System

### Final Architecture Decisions ✅

1. **✅ Full .csproj Projects** - Each extension is a complete .NET solution
2. **✅ Git Repositories** - Each extension in its own GitHub repo
3. **✅ Git Submodules** - Built-in extensions added as submodules
4. **✅ Git Clone** - Community extensions cloned into Extensions/Community/
5. **✅ NuGet Dependencies** - Extensions can use any NuGet packages
6. **✅ Approved Registry** - ApprovedExtensions.json for curated extensions
7. **✅ Permission Integration** - Extensions tied to user roles and permissions
8. **✅ Admin UI** - Install/manage extensions via web interface

### Benefits Recap

1. **✅ Modularity** - Extensions are truly independent modules
2. **✅ Simple Distribution** - Git clone, no packaging complexity
3. **✅ Version Control** - Full git history per extension
4. **✅ Easy Updates** - `git pull` to update
5. **✅ Dependencies** - Extensions can use any NuGet packages they need
6. **✅ Community Friendly** - Standard git workflow
7. **✅ Isolation** - Each extension in separate GitHub repo
8. **✅ Professionalism** - Standard .NET practices
9. **✅ Testing** - Proper unit/integration testing
10. **✅ CI/CD** - GitHub Actions can build & test
11. **✅ Security** - Permission system prevents unauthorized extension access
12. **✅ Curated List** - Approved extensions verified by Dataset Studio team

### Directory Structure (Final)

```
DatasetStudio/
├── src/
│   ├── APIBackend/
│   ├── ClientApp/
│   ├── Core/
│   ├── DTO/
│   └── Extensions/
│       └── SDK/                    # SDK (part of main repo, not NuGet)
│
├── Extensions/
│   ├── BuiltIn/                    # Git submodules (official)
│   │   ├── CoreViewer/             # git submodule
│   │   ├── Creator/                # git submodule
│   │   └── Editor/                 # git submodule
│   │
│   └── Community/                  # Git clones (third-party)
│       ├── CustomExtension/        # git clone
│       └── AnotherExtension/       # git clone
│
├── ApprovedExtensions.json         # Curated extension registry
├── DatasetStudio.sln
└── README.md
```

---

## Next Steps

### Phase 2: Extension Loading & Discovery (4-6 hours)

**Tasks:**
1. Implement manifest scanning in `Extensions/BuiltIn/` and `Extensions/Community/`
2. Implement dependency resolution (topological sort)
3. Wire up extension loading in Program.cs (API and Client)
4. Test with a simple extension

**Deliverables:**
- Extensions auto-discovered on startup
- DLLs loaded from `bin/Release/net8.0/` folders
- Services registered, endpoints configured

### Phase 3: Build First Extension - Editor (8-12 hours)

**Tasks:**
1. Create new GitHub repo: `ds-ext-editor`
2. Add as git submodule to `Extensions/BuiltIn/Editor`
3. Build advanced editing features:
   - Bulk tag editor
   - Batch operations (delete, favorite, etc.)
   - Advanced search/filter
   - Metadata editor
4. Create extension.manifest.json
5. Test loading and functionality

**Deliverables:**
- Working Editor extension
- Demonstrates full extension system capabilities
- Reference implementation for community developers

### Phase 4: Approved Extensions Registry (2-3 hours)

**Tasks:**
1. Create `ApprovedExtensions.json` schema
2. Create admin UI page to browse approved extensions
3. Implement GitHub clone and build logic
4. Add search/filter functionality

**Deliverables:**
- ApprovedExtensions.json with initial entries
- Admin UI for browsing and installing extensions

### Phase 5: Permission System Integration (4-6 hours)

**Tasks:**
1. Create permission database tables (Roles, Permissions, RolePermissions, UserExtensionPermissions)
2. Implement `ExtensionPermissionService`
3. Add permission checks to extension loading
4. Add permission filtering to UI (nav menu, extension pages)
5. Create admin UI for managing user extension permissions

**Deliverables:**
- Full permission system
- Extensions respect user roles
- Admin can grant/revoke extension access per user

### Phase 6: Extension Templates & Documentation (3-4 hours)

**Tasks:**
1. Create example extension template project
2. Write "Create Your First Extension" guide
3. Document extension manifest schema
4. Create video tutorial (optional)

**Deliverables:**
- Template project developers can clone and modify
- Comprehensive documentation

---

## Implementation Questions (ANSWERED)

1. ~~**Distribution:**~~ ✅ Git clone, not NuGet packages
2. ~~**Folder Structure:**~~ ✅ BuiltIn/ and Community/, no User/
3. ~~**Sandboxing:**~~ Use AssemblyLoadContext for isolation? **→ YES** (allows unloading)
4. ~~**Database Migrations:**~~ Should extensions be able to add DB migrations? **→ YES** (declare in manifest)
5. ~~**Updates:**~~ Automatic updates or manual? **→ MANUAL** (git pull or Admin UI button)

**Ready to proceed with implementation?** 🚀

---

## Implementation Timeline

- **Phase 2:** Extension Loading - 4-6 hours
- **Phase 3:** Editor Extension - 8-12 hours
- **Phase 4:** Approved Registry - 2-3 hours
- **Phase 5:** Permissions - 4-6 hours
- **Phase 6:** Templates & Docs - 3-4 hours

**Total:** ~24-34 hours of development

**Estimated Calendar Time:** 1-2 weeks (with testing and iteration)
