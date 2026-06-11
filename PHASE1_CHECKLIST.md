# ✅ Phase 1 Refactor Checklist

Quick reference for completing Phase 1 of the Dataset Studio refactor.

---

## 📋 Pre-Flight

- [x] **Backup created** - Branch: `pre-refactor-backup`
- [x] **Planning docs created**
  - [x] REFACTOR_PLAN.md
  - [x] PHASE1_EXECUTION_GUIDE.md
  - [x] FILE_MIGRATION_MAP.md
  - [x] PHASE1_CHECKLIST.md (this file)
- [ ] **Current state verified**
  - [ ] `dotnet build` succeeds
  - [ ] `dotnet test` passes
  - [ ] Application runs
  - [ ] Can view datasets
  - [ ] Can upload datasets

---

## 🏗️ Phase 1 Tasks

### 1. Directory Structure

- [ ] **Core directories**
  - [ ] src/Core/DomainModels/Datasets/
  - [ ] src/Core/DomainModels/Items/
  - [ ] src/Core/DomainModels/Users/ (TODO)
  - [ ] src/Core/Enumerations/
  - [ ] src/Core/Abstractions/Parsers/
  - [ ] src/Core/Abstractions/Repositories/
  - [ ] src/Core/BusinessLogic/Parsers/
  - [ ] src/Core/BusinessLogic/Modality/
  - [ ] src/Core/BusinessLogic/Layouts/
  - [ ] src/Core/Utilities/Logging/
  - [ ] src/Core/Utilities/Helpers/
  - [ ] src/Core/Constants/

- [ ] **DTO directories**
  - [ ] src/DTO/Common/
  - [ ] src/DTO/Datasets/
  - [ ] src/DTO/Items/
  - [ ] src/DTO/Users/ (TODO)
  - [ ] src/DTO/Extensions/ (TODO)
  - [ ] src/DTO/AI/ (TODO)

- [ ] **APIBackend directories**
  - [ ] src/APIBackend/Configuration/
  - [ ] src/APIBackend/Controllers/
  - [ ] src/APIBackend/Services/DatasetManagement/
  - [ ] src/APIBackend/Services/Integration/
  - [ ] src/APIBackend/DataAccess/LiteDB/Repositories/
  - [ ] src/APIBackend/Models/
  - [ ] src/APIBackend/Endpoints/

- [ ] **ClientApp directories**
  - [ ] src/ClientApp/Configuration/
  - [ ] src/ClientApp/wwwroot/
  - [ ] src/ClientApp/Features/Home/Pages/
  - [ ] src/ClientApp/Features/Datasets/Pages/
  - [ ] src/ClientApp/Features/Datasets/Components/
  - [ ] src/ClientApp/Features/Datasets/Services/
  - [ ] src/ClientApp/Features/Settings/Pages/
  - [ ] src/ClientApp/Features/Settings/Components/
  - [ ] src/ClientApp/Shared/Layout/
  - [ ] src/ClientApp/Shared/Components/
  - [ ] src/ClientApp/Shared/Services/
  - [ ] src/ClientApp/Services/StateManagement/
  - [ ] src/ClientApp/Services/ApiClients/
  - [ ] src/ClientApp/Services/Caching/
  - [ ] src/ClientApp/Services/Interop/

- [ ] **Extensions scaffold**
  - [ ] src/Extensions/SDK/ (TODO)
  - [ ] src/Extensions/BuiltIn/ (TODO)
  - [ ] src/Extensions/UserExtensions/ (TODO)

- [ ] **Documentation**
  - [ ] Docs/ (TODO)
  - [ ] Scripts/ (TODO)

---

### 2. Project Files

- [ ] **Core.csproj**
  - [ ] Create src/Core/Core.csproj
  - [ ] Namespace: DatasetStudio.Core
  - [ ] Add CsvHelper package

- [ ] **DTO.csproj**
  - [ ] Create src/DTO/DTO.csproj
  - [ ] Namespace: DatasetStudio.DTO

- [ ] **APIBackend.csproj**
  - [ ] Create src/APIBackend/APIBackend.csproj
  - [ ] Namespace: DatasetStudio.APIBackend
  - [ ] Add package references (LiteDB, Swashbuckle, CsvHelper, Parquet.Net)
  - [ ] Add project references (Core, DTO, ClientApp)

- [ ] **ClientApp.csproj**
  - [ ] Create src/ClientApp/ClientApp.csproj
  - [ ] Namespace: DatasetStudio.ClientApp
  - [ ] Add package references (Blazor, MudBlazor, Blazored.LocalStorage, CsvHelper)
  - [ ] Add project references (Core, DTO)

- [ ] **Solution file**
  - [ ] Create DatasetStudio.sln
  - [ ] Add all 4 projects
  - [ ] Verify solution builds

---

### 3. Core Migration (35 files)

**Enumerations (4 files)**
- [ ] DatasetFormat.cs → Core/Enumerations/
- [ ] Modality.cs → Core/Enumerations/
- [ ] ViewMode.cs → Core/Enumerations/
- [ ] ThemeMode.cs → Core/Enumerations/

**Constants (3 files)**
- [ ] DatasetFormats.cs → Core/Constants/
- [ ] Modalities.cs → Core/Constants/
- [ ] StorageKeys.cs → Core/Constants/

**Utilities (4 files)**
- [ ] Logs.cs → Core/Utilities/Logging/
- [ ] ImageHelper.cs → Core/Utilities/Helpers/
- [ ] TsvHelper.cs → Core/Utilities/Helpers/
- [ ] ZipHelpers.cs → Core/Utilities/Helpers/

**Domain Models (7 files)**
- [ ] Dataset.cs → Core/DomainModels/Datasets/
- [ ] DatasetItem.cs → Core/DomainModels/Items/
- [ ] ImageItem.cs → Core/DomainModels/Items/
- [ ] FilterCriteria.cs → Core/DomainModels/
- [ ] ViewSettings.cs → Core/DomainModels/
- [ ] Metadata.cs → Core/DomainModels/
- [ ] PagedResult.cs → Core/DomainModels/
- [ ] DatasetFileCollection.cs → Core/DomainModels/
- [ ] EnrichmentFileInfo.cs → Core/DomainModels/
- [ ] ApiKeySettings.cs → Core/DomainModels/

**Abstractions (6 files)**
- [ ] IDatasetParser.cs → Core/Abstractions/Parsers/
- [ ] IDatasetRepository.cs → Core/Abstractions/Repositories/
- [ ] IDatasetItemRepository.cs → Core/Abstractions/Repositories/
- [ ] IModalityProvider.cs → Core/Abstractions/
- [ ] ILayoutProvider.cs → Core/Abstractions/
- [ ] IFormatDetector.cs → Core/Abstractions/
- [ ] IDatasetItem.cs → Core/Abstractions/

**Business Logic (11 files)**
- [ ] ParserRegistry.cs → Core/BusinessLogic/Parsers/
- [ ] UnsplashTsvParser.cs → Core/BusinessLogic/Parsers/
- [ ] BaseTsvParser.cs → Core/BusinessLogic/Parsers/
- [ ] ModalityProviderRegistry.cs → Core/BusinessLogic/Modality/
- [ ] ImageModalityProvider.cs → Core/BusinessLogic/Modality/
- [ ] LayoutRegistry.cs → Core/BusinessLogic/Layouts/
- [ ] LayoutProviders.cs → Core/BusinessLogic/Layouts/
- [ ] DatasetLoader.cs → Core/BusinessLogic/
- [ ] FilterService.cs → Core/BusinessLogic/
- [ ] SearchService.cs → Core/BusinessLogic/
- [ ] EnrichmentMergerService.cs → Core/BusinessLogic/
- [ ] FormatDetector.cs → Core/BusinessLogic/
- [ ] MultiFileDetectorService.cs → Core/BusinessLogic/

**Build Test**
- [ ] `dotnet build src/Core/Core.csproj` succeeds

---

### 4. DTO Migration (9 files)

**Common (3 files)**
- [ ] PageRequest.cs → DTO/Common/
- [ ] PageResponse.cs → DTO/Common/
- [ ] FilterRequest.cs → DTO/Common/

**Datasets (6 files)**
- [ ] DatasetSummaryDto.cs → DTO/Datasets/
- [ ] DatasetDetailDto.cs → DTO/Datasets/
- [ ] DatasetItemDto.cs → DTO/Datasets/
- [ ] CreateDatasetRequest.cs → DTO/Datasets/
- [ ] DatasetSourceType.cs → DTO/Datasets/
- [ ] IngestionStatusDto.cs → DTO/Datasets/

**Items (1 file)**
- [ ] UpdateItemRequest.cs → DTO/Items/

**Build Test**
- [ ] `dotnet build src/DTO/DTO.csproj` succeeds

---

### 5. APIBackend Migration (15 files + endpoints)

**Configuration (3 files)**
- [ ] Program.cs → APIBackend/Configuration/
- [ ] appsettings.json → APIBackend/Configuration/
- [ ] appsettings.Development.json → APIBackend/Configuration/

**Models (4 files)**
- [ ] DatasetEntity.cs → APIBackend/Models/
- [ ] DatasetDiskMetadata.cs → APIBackend/Models/
- [ ] HuggingFaceDatasetInfo.cs → APIBackend/Models/
- [ ] HuggingFaceDatasetProfile.cs → APIBackend/Models/

**Repositories (2 files)**
- [ ] LiteDbDatasetEntityRepository.cs → APIBackend/DataAccess/LiteDB/Repositories/DatasetRepository.cs
- [ ] LiteDbDatasetItemRepository.cs → APIBackend/DataAccess/LiteDB/Repositories/ItemRepository.cs

**Services (6 files)**
- [ ] IDatasetIngestionService.cs → APIBackend/Services/DatasetManagement/
- [ ] DatasetDiskImportService.cs → APIBackend/Services/DatasetManagement/
- [ ] HuggingFaceStreamingStrategy.cs → APIBackend/Services/DatasetManagement/
- [ ] HuggingFaceDatasetServerClient.cs → APIBackend/Services/Integration/
- [ ] HuggingFaceDiscoveryService.cs → APIBackend/Services/Integration/
- [ ] IHuggingFaceClient.cs → APIBackend/Services/Integration/
- [ ] DatasetMappings.cs → APIBackend/Services/Dtos/

**Endpoints → Controllers**
- [ ] Create APIBackend/Controllers/ItemsController.cs (from ItemEditEndpoints.cs)
- [ ] Create APIBackend/Controllers/DatasetsController.cs (new, basic CRUD)

**Extensions**
- [ ] ServiceCollectionExtensions.cs → APIBackend/Extensions/

**Build Test**
- [ ] `dotnet build src/APIBackend/APIBackend.csproj` succeeds

---

### 6. ClientApp Migration (62 files)

**Configuration (3 files)**
- [ ] Program.cs → ClientApp/Configuration/
- [ ] App.razor → ClientApp/Configuration/
- [ ] _Imports.razor → ClientApp/Configuration/

**wwwroot (static files)**
- [ ] index.html → ClientApp/wwwroot/
- [ ] All css/ → ClientApp/wwwroot/css/
- [ ] All js/ → ClientApp/wwwroot/js/

**Features/Home (2 files)**
- [ ] Index.razor → ClientApp/Features/Home/Pages/
- [ ] Index.razor.cs → ClientApp/Features/Home/Pages/

**Features/Datasets (30+ files)**

Pages:
- [ ] MyDatasets.razor → DatasetLibrary.razor
- [ ] MyDatasets.razor.cs → DatasetLibrary.razor.cs
- [ ] DatasetViewer.razor → Features/Datasets/Pages/
- [ ] DatasetViewer.razor.cs → Features/Datasets/Pages/
- [ ] CreateDataset.razor → Features/Datasets/Pages/

Components:
- [ ] DatasetUploader.razor → Features/Datasets/Components/
- [ ] DatasetUploader.razor.cs → Features/Datasets/Components/
- [ ] HuggingFaceDatasetOptions.razor → Features/Datasets/Components/
- [ ] DatasetStats.razor → Features/Datasets/Components/
- [ ] DatasetInfo.razor → Features/Datasets/Components/
- [ ] ImageGrid.razor → Features/Datasets/Components/
- [ ] ImageGrid.razor.cs → Features/Datasets/Components/
- [ ] ImageCard.razor → Features/Datasets/Components/
- [ ] ImageCard.razor.cs → Features/Datasets/Components/
- [ ] ImageList.razor → ImageGallery.razor
- [ ] ViewerContainer.razor → Features/Datasets/Components/
- [ ] ViewerContainer.razor.cs → Features/Datasets/Components/
- [ ] ImageDetailPanel.razor → Features/Datasets/Components/
- [ ] ImageDetailPanel.razor.cs → Features/Datasets/Components/
- [ ] ImageLightbox.razor → Features/Datasets/Components/
- [ ] FilterPanel.razor → Features/Datasets/Components/
- [ ] FilterPanel.razor.cs → Features/Datasets/Components/
- [ ] SearchBar.razor → Features/Datasets/Components/
- [ ] FilterChips.razor → Features/Datasets/Components/
- [ ] DateRangeFilter.razor → Features/Datasets/Components/
- [ ] AddTagDialog.razor → Features/Datasets/Components/

Services:
- [ ] DatasetCacheService.cs → Features/Datasets/Services/
- [ ] ItemEditService.cs → Features/Datasets/Services/

**Features/Settings (5+ files)**
- [ ] Settings.razor → Features/Settings/Pages/
- [ ] ThemeSelector.razor → Features/Settings/Components/
- [ ] LanguageSelector.razor → Features/Settings/Components/
- [ ] ViewPreferences.razor → Features/Settings/Components/
- [ ] ApiKeySettingsPanel.razor → Features/Settings/Components/

**Shared (12+ files)**

Layout:
- [ ] MainLayout.razor → Shared/Layout/
- [ ] MainLayout.razor.cs → Shared/Layout/
- [ ] NavMenu.razor → Shared/Layout/
- [ ] NavMenu.razor.cs → Shared/Layout/

Components:
- [ ] LoadingIndicator.razor → Shared/Components/
- [ ] EmptyState.razor → Shared/Components/
- [ ] ErrorBoundary.razor → Shared/Components/
- [ ] ConfirmDialog.razor → Shared/Components/
- [ ] DatasetSwitcher.razor → Shared/Components/
- [ ] LayoutSwitcher.razor → Shared/Components/

Services:
- [ ] NotificationService.cs → Shared/Services/
- [ ] NavigationService.cs → Shared/Services/

**Services (14 files)**

StateManagement:
- [ ] AppState.cs → Services/StateManagement/
- [ ] DatasetState.cs → Services/StateManagement/
- [ ] FilterState.cs → Services/StateManagement/
- [ ] ViewState.cs → Services/StateManagement/
- [ ] ApiKeyState.cs → Services/StateManagement/

ApiClients:
- [ ] DatasetApiClient.cs → Services/ApiClients/
- [ ] DatasetApiOptions.cs → Services/ApiClients/

Caching:
- [ ] DatasetIndexedDbCache.cs → IndexedDbCache.cs

Interop:
- [ ] IndexedDbInterop.cs → Services/Interop/
- [ ] FileReaderInterop.cs → Services/Interop/
- [ ] ImageLazyLoadInterop.cs → Services/Interop/
- [ ] LocalStorageInterop.cs → Services/Interop/

Extensions:
- [ ] ServiceCollectionExtensions.cs → Extensions/

**Build Test**
- [ ] `dotnet build src/ClientApp/ClientApp.csproj` succeeds

---

### 7. TODO Scaffolds (107 files)

**Core TODOs (25 files)**
- [ ] DomainModels/Users/*.cs (3 files)
- [ ] DomainModels/Items/VideoItem.cs
- [ ] DomainModels/Items/AudioItem.cs
- [ ] DomainModels/Items/Caption.cs
- [ ] Abstractions/Storage/*.cs (1 file)
- [ ] Abstractions/Captioning/*.cs (1 file)
- [ ] Abstractions/Extensions/*.cs (3 files)
- [ ] Abstractions/Repositories/IUserRepository.cs
- [ ] BusinessLogic/Parsers/*.cs (4 TODO files)
- [ ] BusinessLogic/Storage/*.cs (4 files)
- [ ] BusinessLogic/Extensions/*.cs (3 files)
- [ ] Utilities/Encryption/*.cs (1 file)

**DTO TODOs (12 files)**
- [ ] Users/*.cs (4 files)
- [ ] Extensions/*.cs (3 files)
- [ ] AI/*.cs (3 files)
- [ ] Datasets/UpdateDatasetRequest.cs
- [ ] Datasets/ImportRequest.cs

**APIBackend TODOs (18 files)**
- [ ] Controllers/*.cs (4 controllers)
- [ ] Services/DatasetManagement/ParquetDataService.cs
- [ ] Services/Caching/*.cs (1 file)
- [ ] Services/Authentication/*.cs (2 files)
- [ ] Services/Extensions/*.cs (2 files)
- [ ] DataAccess/PostgreSQL/*.cs (5 files)
- [ ] DataAccess/Parquet/*.cs (2 files)
- [ ] Middleware/*.cs (3 files)
- [ ] BackgroundWorkers/*.cs (3 files)

**ClientApp TODOs (28 files)**
- [ ] Features/Installation/*.* (8 files)
- [ ] Features/Authentication/*.* (3 files)
- [ ] Features/Administration/*.* (5 files)
- [ ] Features/Settings/Components/AccountSettings.razor
- [ ] Features/Settings/Components/PrivacySettings.razor
- [ ] Features/Datasets/Components/InlineEditor.razor
- [ ] Features/Datasets/Components/AdvancedSearch.razor
- [ ] Shared/Layout/AdminLayout.razor
- [ ] Shared/Components/Toast.razor
- [ ] Shared/Services/ThemeService.cs
- [ ] Services/StateManagement/UserState.cs
- [ ] Services/StateManagement/ExtensionState.cs
- [ ] Services/ApiClients/*.cs (3 files)
- [ ] Services/Caching/ThumbnailCache.cs
- [ ] Services/Interop/InstallerInterop.cs
- [ ] wwwroot/Themes/*.css (3 files)
- [ ] wwwroot/js/Installer.js

**Extensions TODOs (15 files)**
- [ ] SDK/*.* (4 files)
- [ ] BuiltIn/*/* (11 extension files)
- [ ] UserExtensions/README.md

**Documentation TODOs (9 files)**
- [ ] Docs/Installation/*.md (3 files)
- [ ] Docs/UserGuides/*.md (3 files)
- [ ] Docs/API/*.md (1 file)
- [ ] Docs/Development/*.md (2 files)

---

### 8. Namespace Updates

**Find & Replace in all migrated files:**
- [ ] `HartsysDatasetEditor.Core` → `DatasetStudio.Core`
- [ ] `HartsysDatasetEditor.Contracts` → `DatasetStudio.DTO`
- [ ] `HartsysDatasetEditor.Api` → `DatasetStudio.APIBackend`
- [ ] `HartsysDatasetEditor.Client` → `DatasetStudio.ClientApp`

**Verify:**
- [ ] No references to old namespaces remain
- [ ] All using statements updated
- [ ] All project references updated

---

### 9. Configuration Updates

- [ ] **APIBackend/Configuration/Program.cs**
  - [ ] Update service registrations
  - [ ] Update static file paths
  - [ ] Update CORS settings if needed

- [ ] **ClientApp/Configuration/Program.cs**
  - [ ] Update service registrations
  - [ ] Update base address
  - [ ] Update using statements

- [ ] **ClientApp/Configuration/_Imports.razor**
  - [ ] Update all @using statements
  - [ ] Add new namespace references

- [ ] **ClientApp/wwwroot/index.html**
  - [ ] Update title to "Dataset Studio by Hartsy"
  - [ ] Update meta tags if needed

- [ ] **APIBackend/Configuration/appsettings.json**
  - [ ] Verify paths are correct
  - [ ] Update any hardcoded references

---

### 10. Build & Test

**Incremental Build Tests:**
- [ ] `dotnet build src/Core/Core.csproj` - 0 errors
- [ ] `dotnet build src/DTO/DTO.csproj` - 0 errors
- [ ] `dotnet build src/ClientApp/ClientApp.csproj` - 0 errors
- [ ] `dotnet build src/APIBackend/APIBackend.csproj` - 0 errors
- [ ] `dotnet build DatasetStudio.sln` - 0 errors, 0 warnings

**Test Suite:**
- [ ] `dotnet test` - all tests pass
- [ ] Update test project references
- [ ] Update test namespaces

**Application Testing:**
- [ ] `dotnet run --project src/APIBackend/APIBackend.csproj`
- [ ] Application starts without errors
- [ ] Navigate to homepage
- [ ] View datasets page works
- [ ] Upload local file works
- [ ] Upload ZIP file works
- [ ] Import from HuggingFace works
- [ ] Filter panel works
- [ ] Search works
- [ ] Image detail panel works
- [ ] Edit image metadata works
- [ ] Settings page works
- [ ] Theme switching works
- [ ] View mode switching works

---

### 11. Cleanup

- [ ] **Delete old folders** (after verification)
  - [ ] src/HartsysDatasetEditor.Core/
  - [ ] src/HartsysDatasetEditor.Contracts/
  - [ ] src/HartsysDatasetEditor.Api/
  - [ ] src/HartsysDatasetEditor.Client/

- [ ] **Delete old solution**
  - [ ] HartsysDatasetEditor.sln

- [ ] **Update .gitignore**
  - [ ] Remove old project references
  - [ ] Add new project references if needed

---

### 12. Documentation

- [ ] **Update README.md**
  - [ ] Update project name
  - [ ] Update build instructions
  - [ ] Update project structure
  - [ ] Add link to REFACTOR_PLAN.md

- [ ] **Create ARCHITECTURE.md**
  - [ ] Document new architecture
  - [ ] Explain feature-based organization
  - [ ] Document extension system (high-level)

- [ ] **Update any other docs**
  - [ ] Contributing guide
  - [ ] License file (if project name is mentioned)

---

### 13. Final Verification

- [ ] **Build checks**
  - [ ] Solution builds with 0 errors
  - [ ] Solution builds with 0 warnings
  - [ ] All tests pass

- [ ] **Functionality checks**
  - [ ] All features from checklist work
  - [ ] No console errors
  - [ ] No browser errors
  - [ ] No breaking changes to user experience

- [ ] **Code quality checks**
  - [ ] No TODO comments except in scaffold files
  - [ ] All namespaces consistent
  - [ ] All using statements cleaned up
  - [ ] No dead code

- [ ] **Git checks**
  - [ ] All files committed
  - [ ] Commit message is clear
  - [ ] No merge conflicts
  - [ ] Branch is clean

---

## 🎉 Phase 1 Complete!

When all checkboxes are checked, Phase 1 is complete!

**Next Steps:**
1. Commit all changes with message: `refactor: Complete Phase 1 - Project restructure and scaffolding`
2. Create PR for review (optional)
3. Celebrate! 🎊
4. Plan Phase 2: Database Migration

---

## 📊 Progress Tracking

**Files Migrated:** ___ / 125
**New Files Created:** ___ / 24
**TODO Scaffolds Created:** ___ / 107
**Total Progress:** ___% (out of 256 files)

---

## 🚨 Issue Tracker

Use this space to note any issues encountered:

```
Issue #1:
- Problem:
- Solution:

Issue #2:
- Problem:
- Solution:
```

---

*Last Updated: 2025-12-08*
*Phase: 1 - Restructure & Scaffold*
*Status: Ready to Execute*
