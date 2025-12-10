# 🔄 Dataset Studio by Hartsy - Complete Refactor Plan

## 📋 Overview

This document outlines the complete refactor from **HartsysDatasetEditor** to **Dataset Studio by Hartsy**.

### Goals
1. ✅ Rename & rebrand to "Dataset Studio by Hartsy"
2. ✅ Create modular extension-based architecture
3. ✅ Implement feature-based organization
4. ✅ Migrate from LiteDB to PostgreSQL + Parquet hybrid
5. ✅ Add multi-user support with authentication
6. ✅ Build installation wizard
7. ✅ Support third-party extensions

---

## 🎯 Phase 1: Project Restructure & Scaffolding (CURRENT PHASE)

### What We're Doing Now
- Creating new directory structure
- Renaming projects and namespaces
- Moving existing working code to new locations
- Creating scaffold files with TODOs for future work
- Ensuring the app still builds and runs

### What We're NOT Doing Yet
- PostgreSQL migration (keeping LiteDB for now)
- Extension system implementation
- Installation wizard
- Multi-user authentication
- AI Tools
- Advanced editing features

---

## 📁 New Project Structure

```
DatasetStudio/
├── src/
│   ├── Core/                                    # Shared domain logic (FROM: HartsysDatasetEditor.Core)
│   ├── DTO/                                     # Data Transfer Objects (FROM: HartsysDatasetEditor.Contracts)
│   ├── APIBackend/                              # API Backend (FROM: HartsysDatasetEditor.Api)
│   ├── ClientApp/                               # Blazor WASM (FROM: HartsysDatasetEditor.Client)
│   └── Extensions/                              # NEW - Extension system scaffold
│
├── tests/
│   └── (existing tests migrated)
│
├── Docs/                                        # NEW - Documentation
├── Scripts/                                     # NEW - Setup scripts
└── REFACTOR_PLAN.md                            # This file
```

---

## 📦 Phase 1 Detailed Task List

### 1.1 Create New Directory Structure ✅

**New Folders to Create:**
```
src/Core/
src/DTO/
src/APIBackend/
src/ClientApp/
src/Extensions/
    ├── SDK/
    ├── BuiltIn/
    │   ├── CoreViewer/
    │   ├── Creator/
    │   ├── Editor/
    │   ├── AITools/
    │   └── AdvancedTools/
    └── UserExtensions/
Docs/
Scripts/
```

### 1.2 Create New Project Files

**Projects to Create:**

1. **Core.csproj** (was HartsysDatasetEditor.Core.csproj)
   - Namespace: `DatasetStudio.Core`
   - Contains: Domain models, interfaces, business logic, utilities

2. **DTO.csproj** (was HartsysDatasetEditor.Contracts.csproj)
   - Namespace: `DatasetStudio.DTO`
   - Contains: All DTOs for API ↔ Client communication

3. **APIBackend.csproj** (was HartsysDatasetEditor.Api.csproj)
   - Namespace: `DatasetStudio.APIBackend`
   - Contains: Controllers, services, repositories, endpoints

4. **ClientApp.csproj** (was HartsysDatasetEditor.Client.csproj)
   - Namespace: `DatasetStudio.ClientApp`
   - Contains: Blazor WASM app, components, pages, services

5. **Extensions.SDK.csproj** (NEW - scaffold only)
   - Namespace: `DatasetStudio.Extensions.SDK`
   - Contains: Base classes for extension development

### 1.3 Migrate Existing Code

#### Core/ Migration

**FROM: src/HartsysDatasetEditor.Core/**

```
Models/ → Core/DomainModels/
├── Dataset.cs → Core/DomainModels/Datasets/Dataset.cs
├── DatasetItem.cs → Core/DomainModels/Items/DatasetItem.cs
├── ImageItem.cs → Core/DomainModels/Items/ImageItem.cs
├── FilterCriteria.cs → Core/DomainModels/FilterCriteria.cs
└── ViewSettings.cs → Core/DomainModels/ViewSettings.cs

Enums/ → Core/Enumerations/
├── DatasetFormat.cs → Core/Enumerations/DatasetFormat.cs
├── Modality.cs → Core/Enumerations/Modality.cs
├── ViewMode.cs → Core/Enumerations/ViewMode.cs
└── ThemeMode.cs → Core/Enumerations/ThemeMode.cs

Interfaces/ → Core/Abstractions/
├── IDatasetParser.cs → Core/Abstractions/Parsers/IDatasetParser.cs
├── IDatasetRepository.cs → Core/Abstractions/Repositories/IDatasetRepository.cs
├── IDatasetItemRepository.cs → Core/Abstractions/Repositories/IDatasetItemRepository.cs
├── IModalityProvider.cs → Core/Abstractions/IModalityProvider.cs
└── ILayoutProvider.cs → Core/Abstractions/ILayoutProvider.cs

Services/ → Core/BusinessLogic/
├── Parsers/
│   ├── ParserRegistry.cs → Core/BusinessLogic/Parsers/ParserRegistry.cs
│   ├── UnsplashTsvParser.cs → Core/BusinessLogic/Parsers/UnsplashCsvParser.cs
│   └── BaseTsvParser.cs → Core/BusinessLogic/Parsers/BaseTsvParser.cs
├── Providers/
│   ├── ImageModalityProvider.cs → Core/BusinessLogic/Modality/ImageModalityProvider.cs
│   └── ModalityProviderRegistry.cs → Core/BusinessLogic/Modality/ModalityProviderRegistry.cs
├── Layouts/
│   ├── LayoutProviders.cs → Core/BusinessLogic/Layouts/LayoutProviders.cs
│   └── LayoutRegistry.cs → Core/BusinessLogic/Layouts/LayoutRegistry.cs
├── DatasetLoader.cs → Core/BusinessLogic/DatasetLoader.cs
├── FilterService.cs → Core/BusinessLogic/FilterService.cs
├── SearchService.cs → Core/BusinessLogic/SearchService.cs
└── EnrichmentMergerService.cs → Core/BusinessLogic/EnrichmentMergerService.cs

Utilities/ → Core/Utilities/
├── ImageHelper.cs → Core/Utilities/Helpers/ImageHelper.cs
├── TsvHelper.cs → Core/Utilities/Helpers/TsvHelper.cs
├── ZipHelpers.cs → Core/Utilities/Helpers/ZipHelpers.cs
└── Logs.cs → Core/Utilities/Logging/Logs.cs

Constants/ → Core/Constants/
├── DatasetFormats.cs → Core/Constants/DatasetFormats.cs
├── Modalities.cs → Core/Constants/Modalities.cs
└── StorageKeys.cs → Core/Constants/StorageKeys.cs
```

#### DTO/ Migration

**FROM: src/HartsysDatasetEditor.Contracts/**

```
Common/
├── PageRequest.cs → DTO/Common/PageRequest.cs
├── PageResponse.cs → DTO/Common/PageResponse.cs
├── FilterRequest.cs → DTO/Common/FilterRequest.cs
└── ApiResponse.cs → DTO/Common/ApiResponse.cs (NEW - TODO)

Datasets/
├── DatasetSummaryDto.cs → DTO/Datasets/DatasetSummaryDto.cs
├── DatasetDetailDto.cs → DTO/Datasets/DatasetDetailDto.cs
├── DatasetItemDto.cs → DTO/Datasets/DatasetItemDto.cs
├── CreateDatasetRequest.cs → DTO/Datasets/CreateDatasetRequest.cs
├── UpdateDatasetRequest.cs → DTO/Datasets/UpdateDatasetRequest.cs (NEW - TODO)
└── IngestionStatusDto.cs → DTO/Datasets/IngestionStatusDto.cs

Items/
└── UpdateItemRequest.cs → DTO/Items/UpdateItemRequest.cs

Users/ (NEW - all TODOs for Phase 2)
├── UserDto.cs (TODO)
├── RegisterRequest.cs (TODO)
├── LoginRequest.cs (TODO)
└── UserSettingsDto.cs (TODO)

Extensions/ (NEW - all TODOs for Phase 3)
├── ExtensionInfoDto.cs (TODO)
├── InstallExtensionRequest.cs (TODO)
└── ExtensionSettingsDto.cs (TODO)

AI/ (NEW - all TODOs for Phase 5)
├── CaptionRequest.cs (TODO)
├── CaptionResponse.cs (TODO)
└── CaptionScore.cs (TODO)
```

#### APIBackend/ Migration

**FROM: src/HartsysDatasetEditor.Api/**

```
Configuration/
├── Program.cs → APIBackend/Configuration/Program.cs
├── appsettings.json → APIBackend/Configuration/appsettings.json
└── appsettings.Development.json → APIBackend/Configuration/appsettings.Development.json

Controllers/ (NEW - will convert endpoints to controllers)
├── DatasetsController.cs (TODO - migrate from endpoints)
├── ItemsController.cs (TODO - migrate from endpoints)
└── UsersController.cs (TODO - Phase 2)
└── ExtensionsController.cs (TODO - Phase 3)
└── AIController.cs (TODO - Phase 5)
└── AdminController.cs (TODO - Phase 2)

Services/
├── DatasetManagement/
│   ├── DatasetService.cs (TODO - refactor from existing)
│   ├── IngestionService.cs → APIBackend/Services/DatasetManagement/IngestionService.cs
│   └── ParquetDataService.cs (TODO - Phase 2)
├── Caching/
│   └── CachingService.cs (TODO - Phase 4)
├── Authentication/ (TODO - Phase 2)
│   ├── UserService.cs (TODO)
│   └── AuthService.cs (TODO)
└── Extensions/ (TODO - Phase 3)
    ├── ExtensionLoaderService.cs (TODO)
    └── ExtensionHostService.cs (TODO)

DataAccess/
├── LiteDB/ (TEMPORARY - keep for Phase 1)
│   └── Repositories/
│       ├── LiteDbDatasetEntityRepository.cs → APIBackend/DataAccess/LiteDB/Repositories/DatasetRepository.cs
│       └── LiteDbDatasetItemRepository.cs → APIBackend/DataAccess/LiteDB/Repositories/ItemRepository.cs
└── PostgreSQL/ (TODO - Phase 2)
    ├── Repositories/
    │   ├── DatasetRepository.cs (TODO)
    │   ├── UserRepository.cs (TODO)
    │   └── ItemRepository.cs (TODO)
    ├── DbContext.cs (TODO)
    └── Migrations/ (TODO)
└── Parquet/ (TODO - Phase 2)
    ├── ParquetItemRepository.cs (TODO)
    └── ParquetWriter.cs (TODO)

Endpoints/ (will migrate to Controllers)
├── ItemEditEndpoints.cs → migrate to ItemsController.cs (TODO)

Models/ (internal API models)
├── DatasetEntity.cs → APIBackend/Models/DatasetEntity.cs
├── DatasetDiskMetadata.cs → APIBackend/Models/DatasetDiskMetadata.cs
├── HuggingFaceDatasetInfo.cs → APIBackend/Models/HuggingFaceDatasetInfo.cs
└── HuggingFaceDatasetProfile.cs → APIBackend/Models/HuggingFaceDatasetProfile.cs

Middleware/ (TODO - Phase 2+)
├── AuthenticationMiddleware.cs (TODO)
├── RateLimitingMiddleware.cs (TODO)
└── ErrorHandlingMiddleware.cs (TODO)

BackgroundWorkers/ (TODO - Phase 4+)
├── IngestionWorker.cs (TODO)
├── ThumbnailGenerationWorker.cs (TODO)
└── CacheWarmupWorker.cs (TODO)
```

#### ClientApp/ Migration

**FROM: src/HartsysDatasetEditor.Client/**

```
Configuration/
├── Program.cs → ClientApp/Configuration/Program.cs
├── App.razor → ClientApp/Configuration/App.razor
└── _Imports.razor → ClientApp/Configuration/_Imports.razor

wwwroot/
├── index.html → ClientApp/wwwroot/index.html
└── (all static assets) → ClientApp/wwwroot/

Features/
├── Home/
│   └── Pages/
│       └── Index.razor → ClientApp/Features/Home/Pages/Index.razor
│
├── Installation/ (TODO - Phase 4)
│   ├── Pages/
│   │   └── Install.razor (TODO)
│   ├── Components/
│   │   ├── WelcomeStep.razor (TODO)
│   │   ├── DeploymentModeStep.razor (TODO)
│   │   ├── AdminAccountStep.razor (TODO)
│   │   ├── ExtensionSelectionStep.razor (TODO)
│   │   ├── StorageConfigStep.razor (TODO)
│   │   └── CompletionStep.razor (TODO)
│   └── Services/
│       └── InstallationService.cs (TODO)
│
├── Datasets/
│   ├── Pages/
│   │   ├── DatasetLibrary.razor → ClientApp/Features/Datasets/Pages/DatasetLibrary.razor (was MyDatasets.razor)
│   │   └── DatasetViewer.razor → ClientApp/Features/Datasets/Pages/DatasetViewer.razor
│   ├── Components/
│   │   ├── DatasetCard.razor (TODO - extract from library page)
│   │   ├── DatasetUploader.razor → ClientApp/Features/Datasets/Components/DatasetUploader.razor
│   │   ├── DatasetStats.razor → ClientApp/Features/Datasets/Components/DatasetStats.razor
│   │   ├── ImageGrid.razor → ClientApp/Features/Datasets/Components/ImageGrid.razor
│   │   ├── ImageCard.razor → ClientApp/Features/Datasets/Components/ImageCard.razor
│   │   ├── ImageGallery.razor (TODO - rename/refactor from ImageList.razor)
│   │   ├── ImageDetail.razor (TODO - extract from viewer)
│   │   ├── InlineEditor.razor (TODO - Phase 5)
│   │   ├── FilterPanel.razor → ClientApp/Features/Datasets/Components/FilterPanel.razor
│   │   └── AdvancedSearch.razor (TODO - enhance FilterPanel)
│   └── Services/
│       └── DatasetCacheService.cs → ClientApp/Features/Datasets/Services/DatasetCacheService.cs
│
├── Authentication/ (TODO - Phase 2)
│   ├── Pages/
│   │   └── Login.razor (TODO)
│   └── Components/
│       ├── LoginForm.razor (TODO)
│       └── RegisterForm.razor (TODO)
│
├── Administration/ (TODO - Phase 2)
│   ├── Pages/
│   │   └── Admin.razor (TODO)
│   └── Components/
│       ├── UserManagement.razor (TODO)
│       ├── ExtensionManager.razor (TODO)
│       ├── SystemSettings.razor (TODO)
│       └── Analytics.razor (TODO)
│
└── Settings/
    ├── Pages/
    │   └── Settings.razor → ClientApp/Features/Settings/Pages/Settings.razor
    └── Components/
        ├── AppearanceSettings.razor → ClientApp/Features/Settings/Components/AppearanceSettings.razor (extract from Settings page)
        ├── AccountSettings.razor (TODO - Phase 2)
        └── PrivacySettings.razor (TODO - Phase 2)

Shared/
├── Layout/
│   ├── MainLayout.razor → ClientApp/Shared/Layout/MainLayout.razor
│   ├── NavMenu.razor → ClientApp/Shared/Layout/NavMenu.razor
│   └── AdminLayout.razor (TODO - Phase 2)
├── Components/
│   ├── LoadingIndicator.razor → ClientApp/Shared/Components/LoadingIndicator.razor
│   ├── EmptyState.razor → ClientApp/Shared/Components/EmptyState.razor
│   ├── ErrorBoundary.razor → ClientApp/Shared/Components/ErrorBoundary.razor
│   ├── ConfirmDialog.razor → ClientApp/Shared/Components/ConfirmDialog.razor
│   └── Toast.razor (TODO - integrate NotificationService)
└── Services/
    ├── NotificationService.cs → ClientApp/Shared/Services/NotificationService.cs
    └── ThemeService.cs (TODO - extract from AppState)

Services/ (Global app-wide services)
├── StateManagement/
│   ├── AppState.cs → ClientApp/Services/StateManagement/AppState.cs
│   ├── UserState.cs (TODO - Phase 2)
│   ├── DatasetState.cs → ClientApp/Services/StateManagement/DatasetState.cs
│   ├── FilterState.cs → ClientApp/Services/StateManagement/FilterState.cs
│   ├── ViewState.cs → ClientApp/Services/StateManagement/ViewState.cs
│   ├── ApiKeyState.cs → ClientApp/Services/StateManagement/ApiKeyState.cs
│   └── ExtensionState.cs (TODO - Phase 3)
├── ApiClients/
│   ├── DatasetApiClient.cs → ClientApp/Services/ApiClients/DatasetApiClient.cs
│   ├── UserApiClient.cs (TODO - Phase 2)
│   ├── ExtensionApiClient.cs (TODO - Phase 3)
│   └── AIApiClient.cs (TODO - Phase 5)
├── Caching/
│   ├── IndexedDbCache.cs → ClientApp/Services/Caching/IndexedDbCache.cs (was DatasetIndexedDbCache.cs)
│   └── ThumbnailCache.cs (TODO - Phase 4)
└── Interop/
    ├── IndexedDbInterop.cs → ClientApp/Services/Interop/IndexedDbInterop.cs
    ├── FileReaderInterop.cs → ClientApp/Services/Interop/FileReaderInterop.cs
    ├── ImageLazyLoadInterop.cs → ClientApp/Services/Interop/ImageLazyLoadInterop.cs
    ├── LocalStorageInterop.cs → ClientApp/Services/Interop/LocalStorageInterop.cs
    └── InstallerInterop.cs (TODO - Phase 4)
```

#### Extensions/ Scaffold (All TODOs)

```
Extensions/
├── SDK/
│   ├── BaseExtension.cs (TODO - Phase 3)
│   ├── ExtensionMetadata.cs (TODO - Phase 3)
│   ├── ExtensionManifest.cs (TODO - Phase 3)
│   └── DevelopmentGuide.md (TODO - Phase 3)
│
├── BuiltIn/
│   ├── CoreViewer/
│   │   ├── extension.manifest.json (TODO - Phase 3)
│   │   ├── CoreViewerExtension.cs (TODO - Phase 3)
│   │   ├── Components/ (TODO)
│   │   ├── Services/ (TODO)
│   │   └── Assets/ (TODO)
│   │
│   ├── Creator/
│   │   ├── extension.manifest.json (TODO - Phase 3)
│   │   ├── CreatorExtension.cs (TODO - Phase 3)
│   │   └── (migrate DatasetUploader + import logic) (TODO)
│   │
│   ├── Editor/
│   │   ├── extension.manifest.json (TODO - Phase 5)
│   │   ├── EditorExtension.cs (TODO - Phase 5)
│   │   └── (TODO)
│   │
│   ├── AITools/
│   │   ├── extension.manifest.json (TODO - Phase 5)
│   │   ├── AIToolsExtension.cs (TODO - Phase 5)
│   │   └── (TODO)
│   │
│   └── AdvancedTools/
│       ├── extension.manifest.json (TODO - Phase 6)
│       ├── AdvancedToolsExtension.cs (TODO - Phase 6)
│       └── (TODO)
│
└── UserExtensions/
    └── README.md (TODO - Phase 3)
```

---

## 🔧 Phase 1 Implementation Steps

### Step 1: Backup Current Code ✅
```bash
git add .
git commit -m "Backup before refactor - current working state"
git branch pre-refactor-backup
```

### Step 2: Create New Directory Structure
- Create all new folders in src/
- Create Extensions/ folder structure
- Create Docs/ and Scripts/ folders

### Step 3: Create New Project Files
- Create Core.csproj
- Create DTO.csproj
- Create APIBackend.csproj
- Create ClientApp.csproj
- Update solution file

### Step 4: Copy & Migrate Files
- Copy files from old structure to new structure
- Update namespaces in all files
- Update project references
- Update using statements

### Step 5: Update Configuration
- Update appsettings.json paths
- Update wwwroot references
- Update Program.cs service registrations
- Update _Imports.razor

### Step 6: Create TODO Scaffold Files
- Create placeholder files with TODO comments
- Add summary comments explaining future functionality
- Ensure code compiles with empty/stub implementations

### Step 7: Build & Test
- Build solution
- Fix any compilation errors
- Run application
- Verify existing features still work
- Test dataset viewing
- Test dataset upload

### Step 8: Clean Up Old Files
- Delete old project folders (after verifying new structure works)
- Update .gitignore
- Update README.md

---

## 📝 Namespace Migration Map

| Old Namespace | New Namespace |
|---------------|---------------|
| `HartsysDatasetEditor.Core` | `DatasetStudio.Core` |
| `HartsysDatasetEditor.Core.Models` | `DatasetStudio.Core.DomainModels` |
| `HartsysDatasetEditor.Core.Interfaces` | `DatasetStudio.Core.Abstractions` |
| `HartsysDatasetEditor.Core.Services` | `DatasetStudio.Core.BusinessLogic` |
| `HartsysDatasetEditor.Contracts` | `DatasetStudio.DTO` |
| `HartsysDatasetEditor.Api` | `DatasetStudio.APIBackend` |
| `HartsysDatasetEditor.Client` | `DatasetStudio.ClientApp` |

---

## 🎯 Future Phases (After Phase 1)

### Phase 2: Database Migration (PostgreSQL + Parquet)
- Set up PostgreSQL with Entity Framework Core
- Create database schema (users, datasets, captions, permissions)
- Implement Parquet read/write for dataset items
- Create migration scripts from LiteDB to PostgreSQL
- Update repositories to use new storage

### Phase 3: Extension System
- Build Extension SDK base classes
- Create ExtensionRegistry and loader
- Implement dynamic assembly loading
- Convert existing features to extensions
- Test hot-loading extensions

### Phase 4: Installation Wizard
- Build wizard UI components (7 steps)
- Implement extension downloader
- Add AI model download logic
- Create setup configuration
- Test installation flow

### Phase 5: Authentication & Multi-User
- Implement JWT authentication
- Create user management system
- Add role-based access control
- Build admin dashboard
- Add per-dataset permissions

### Phase 6: AI Tools Extension
- Integrate BLIP/CLIP models
- Add OpenAI/Anthropic API support
- Build caption scoring system
- Create batch processing pipeline

### Phase 7: Advanced Tools Extension
- Dataset format conversion
- Dataset merging
- Deduplication
- Quality analysis

### Phase 8: Testing & Polish
- Integration testing
- Performance optimization
- UI/UX refinements
- Documentation
- Bug fixes

---

## ✅ Phase 1 Success Criteria

Phase 1 is complete when:

1. ✅ New directory structure created
2. ✅ All projects renamed and building successfully
3. ✅ All namespaces updated
4. ✅ Existing features still work (dataset viewing, upload)
5. ✅ Application runs without errors
6. ✅ All future features have TODO scaffolds
7. ✅ Code is well-documented
8. ✅ README.md updated
9. ✅ Old project folders removed
10. ✅ Git history preserved

---

## 🚨 Important Notes for Phase 1

### Keep Working:
- ✅ Dataset viewing (grid/list)
- ✅ Dataset upload (local files, ZIP, HuggingFace)
- ✅ Filtering and search
- ✅ Image detail panel
- ✅ Settings (theme, view preferences)
- ✅ API key management
- ✅ LiteDB storage (temporary)

### Add as TODOs (Not Implementing Yet):
- ❌ PostgreSQL
- ❌ Parquet storage
- ❌ Authentication/users
- ❌ Extension system
- ❌ Installation wizard
- ❌ AI tools
- ❌ Advanced editing
- ❌ Multi-user features

### Key Principle:
**"Move, don't break"** - We're reorganizing the codebase, not rewriting it. The app should work the same at the end of Phase 1, just with better organization.

---

## 📚 Documentation to Create

- [x] REFACTOR_PLAN.md (this file)
- [ ] ARCHITECTURE.md (Phase 1)
- [ ] Docs/Installation/QuickStart.md (Phase 4)
- [ ] Docs/Development/ExtensionDevelopment.md (Phase 3)
- [ ] Extensions/SDK/DevelopmentGuide.md (Phase 3)
- [ ] Update README.md (Phase 1)

---

## 🎉 Expected Outcome After Phase 1

A well-organized, modular codebase with:
- Clear separation of concerns
- Feature-based organization
- Professional naming conventions
- Comprehensive TODOs for future work
- Working baseline functionality
- Easy to navigate structure
- Ready for extension system implementation

**Current App:** Monolithic "HartsysDatasetEditor"
**After Phase 1:** Modular "Dataset Studio by Hartsy" (with working baseline)
**After All Phases:** Professional ML dataset management platform with extensions

---

*Last Updated: 2025-12-08*
*Status: Phase 1 - In Progress*
