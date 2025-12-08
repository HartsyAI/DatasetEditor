# 📋 File Migration Map - Complete Reference

This document lists every file migration for Phase 1 refactor.

---

## Legend
- ✅ = File exists and needs migration
- 🆕 = New file to create
- 📝 = TODO scaffold (create empty with comments)
- ❌ = Will be deleted after migration

---

## Core Project Migration

### Source: `src/HartsysDatasetEditor.Core/` → Target: `src/Core/`

| Old Path | New Path | Status | Notes |
|----------|----------|--------|-------|
| **Enumerations** |
| `Enums/DatasetFormat.cs` | `Enumerations/DatasetFormat.cs` | ✅ | Update namespace |
| `Enums/Modality.cs` | `Enumerations/Modality.cs` | ✅ | Update namespace |
| `Enums/ViewMode.cs` | `Enumerations/ViewMode.cs` | ✅ | Update namespace |
| `Enums/ThemeMode.cs` | `Enumerations/ThemeMode.cs` | ✅ | Update namespace |
| 🆕 | `Enumerations/UserRole.cs` | 📝 | TODO Phase 2 |
| 🆕 | `Enumerations/ExtensionType.cs` | 📝 | TODO Phase 3 |
| 🆕 | `Enumerations/IngestionStatus.cs` | 📝 | TODO Phase 2 |
| **Constants** |
| `Constants/DatasetFormats.cs` | `Constants/DatasetFormats.cs` | ✅ | Update namespace |
| `Constants/Modalities.cs` | `Constants/Modalities.cs` | ✅ | Update namespace |
| `Constants/StorageKeys.cs` | `Constants/StorageKeys.cs` | ✅ | Update namespace |
| 🆕 | `Constants/Extensions.cs` | 📝 | TODO Phase 3 |
| **Domain Models** |
| `Models/Dataset.cs` | `DomainModels/Datasets/Dataset.cs` | ✅ | Update namespace |
| 🆕 | `DomainModels/Datasets/DatasetMetadata.cs` | 📝 | TODO Phase 2 |
| 🆕 | `DomainModels/Datasets/DatasetPermission.cs` | 📝 | TODO Phase 2 |
| `Models/DatasetItem.cs` | `DomainModels/Items/DatasetItem.cs` | ✅ | Update namespace |
| `Models/ImageItem.cs` | `DomainModels/Items/ImageItem.cs` | ✅ | Update namespace |
| 🆕 | `DomainModels/Items/VideoItem.cs` | 📝 | TODO Phase 6 |
| 🆕 | `DomainModels/Items/AudioItem.cs` | 📝 | TODO Phase 7 |
| 🆕 | `DomainModels/Items/Caption.cs` | 📝 | TODO Phase 5 |
| 🆕 | `DomainModels/Users/User.cs` | 📝 | TODO Phase 2 |
| 🆕 | `DomainModels/Users/UserSettings.cs` | 📝 | TODO Phase 2 |
| 🆕 | `DomainModels/Users/Permission.cs` | 📝 | TODO Phase 2 |
| `Models/FilterCriteria.cs` | `DomainModels/FilterCriteria.cs` | ✅ | Update namespace |
| `Models/ViewSettings.cs` | `DomainModels/ViewSettings.cs` | ✅ | Update namespace |
| `Models/Metadata.cs` | `DomainModels/Metadata.cs` | ✅ | Update namespace |
| `Models/PagedResult.cs` | `DomainModels/PagedResult.cs` | ✅ | Update namespace |
| `Models/DatasetFileCollection.cs` | `DomainModels/DatasetFileCollection.cs` | ✅ | Update namespace |
| `Models/EnrichmentFileInfo.cs` | `DomainModels/EnrichmentFileInfo.cs` | ✅ | Update namespace |
| `Models/ApiKeySettings.cs` | `DomainModels/ApiKeySettings.cs` | ✅ | Update namespace |
| **Abstractions/Interfaces** |
| `Interfaces/IDatasetParser.cs` | `Abstractions/Parsers/IDatasetParser.cs` | ✅ | Update namespace |
| 🆕 | `Abstractions/Storage/IStorageProvider.cs` | 📝 | TODO Phase 2 |
| 🆕 | `Abstractions/Captioning/ICaptioningEngine.cs` | 📝 | TODO Phase 5 |
| 🆕 | `Abstractions/Extensions/IExtension.cs` | 📝 | TODO Phase 3 |
| 🆕 | `Abstractions/Extensions/IExtensionMetadata.cs` | 📝 | TODO Phase 3 |
| 🆕 | `Abstractions/Extensions/IExtensionRegistry.cs` | 📝 | TODO Phase 3 |
| `Interfaces/IDatasetRepository.cs` | `Abstractions/Repositories/IDatasetRepository.cs` | ✅ | Update namespace |
| `Interfaces/IDatasetItemRepository.cs` | `Abstractions/Repositories/IDatasetItemRepository.cs` | ✅ | Update namespace |
| 🆕 | `Abstractions/Repositories/IUserRepository.cs` | 📝 | TODO Phase 2 |
| `Interfaces/IModalityProvider.cs` | `Abstractions/IModalityProvider.cs` | ✅ | Update namespace |
| `Interfaces/ILayoutProvider.cs` | `Abstractions/ILayoutProvider.cs` | ✅ | Update namespace |
| `Interfaces/IFormatDetector.cs` | `Abstractions/IFormatDetector.cs` | ✅ | Update namespace |
| `Interfaces/IDatasetItem.cs` | `Abstractions/IDatasetItem.cs` | ✅ | Update namespace |
| **Business Logic** |
| `Services/Parsers/ParserRegistry.cs` | `BusinessLogic/Parsers/ParserRegistry.cs` | ✅ | Update namespace |
| `Services/Parsers/UnsplashTsvParser.cs` | `BusinessLogic/Parsers/UnsplashTsvParser.cs` | ✅ | Update namespace |
| `Services/Parsers/BaseTsvParser.cs` | `BusinessLogic/Parsers/BaseTsvParser.cs` | ✅ | Update namespace |
| 🆕 | `BusinessLogic/Parsers/CocoJsonParser.cs` | 📝 | TODO Phase 6 |
| 🆕 | `BusinessLogic/Parsers/YoloParser.cs` | 📝 | TODO Phase 6 |
| 🆕 | `BusinessLogic/Parsers/ParquetParser.cs` | 📝 | TODO Phase 2 |
| 🆕 | `BusinessLogic/Parsers/HuggingFaceParser.cs` | 📝 | TODO Phase 6 |
| 🆕 | `BusinessLogic/Storage/LocalStorageProvider.cs` | 📝 | TODO Phase 2 |
| 🆕 | `BusinessLogic/Storage/S3StorageProvider.cs` | 📝 | TODO Phase 6 |
| 🆕 | `BusinessLogic/Storage/AzureBlobProvider.cs` | 📝 | TODO Phase 7 |
| 🆕 | `BusinessLogic/Storage/HartsyCloudProvider.cs` | 📝 | TODO Phase 7 |
| `Services/Providers/ModalityProviderRegistry.cs` | `BusinessLogic/Modality/ModalityProviderRegistry.cs` | ✅ | Update namespace |
| `Services/Providers/ImageModalityProvider.cs` | `BusinessLogic/Modality/ImageModalityProvider.cs` | ✅ | Update namespace |
| 🆕 | `BusinessLogic/Modality/VideoModalityProvider.cs` | 📝 | TODO Phase 6 |
| `Services/Layouts/LayoutRegistry.cs` | `BusinessLogic/Layouts/LayoutRegistry.cs` | ✅ | Update namespace |
| `Services/Layouts/LayoutProviders.cs` | `BusinessLogic/Layouts/LayoutProviders.cs` | ✅ | Update namespace |
| 🆕 | `BusinessLogic/Extensions/ExtensionRegistry.cs` | 📝 | TODO Phase 3 |
| 🆕 | `BusinessLogic/Extensions/ExtensionLoader.cs` | 📝 | TODO Phase 3 |
| 🆕 | `BusinessLogic/Extensions/ExtensionValidator.cs` | 📝 | TODO Phase 3 |
| `Services/DatasetLoader.cs` | `BusinessLogic/DatasetLoader.cs` | ✅ | Update namespace |
| `Services/FilterService.cs` | `BusinessLogic/FilterService.cs` | ✅ | Update namespace |
| `Services/SearchService.cs` | `BusinessLogic/SearchService.cs` | ✅ | Update namespace |
| `Services/EnrichmentMergerService.cs` | `BusinessLogic/EnrichmentMergerService.cs` | ✅ | Update namespace |
| `Services/FormatDetector.cs` | `BusinessLogic/FormatDetector.cs` | ✅ | Update namespace |
| `Services/MultiFileDetectorService.cs` | `BusinessLogic/MultiFileDetectorService.cs` | ✅ | Update namespace |
| **Utilities** |
| `Utilities/Logs.cs` | `Utilities/Logging/Logs.cs` | ✅ | Update namespace |
| `Utilities/ImageHelper.cs` | `Utilities/Helpers/ImageHelper.cs` | ✅ | Update namespace |
| `Utilities/TsvHelper.cs` | `Utilities/Helpers/TsvHelper.cs` | ✅ | Update namespace |
| `Utilities/ZipHelpers.cs` | `Utilities/Helpers/ZipHelpers.cs` | ✅ | Update namespace |
| 🆕 | `Utilities/Helpers/ParquetHelper.cs` | 📝 | TODO Phase 2 |
| 🆕 | `Utilities/Helpers/ShardingHelper.cs` | 📝 | TODO Phase 2 |
| 🆕 | `Utilities/Encryption/ApiKeyEncryption.cs` | 📝 | TODO Phase 2 |

---

## DTO Project Migration

### Source: `src/HartsysDatasetEditor.Contracts/` → Target: `src/DTO/`

| Old Path | New Path | Status | Notes |
|----------|----------|--------|-------|
| **Common** |
| `Common/PageRequest.cs` | `Common/PageRequest.cs` | ✅ | Update namespace |
| `Common/PageResponse.cs` | `Common/PageResponse.cs` | ✅ | Update namespace |
| `Common/FilterRequest.cs` | `Common/FilterRequest.cs` | ✅ | Update namespace |
| 🆕 | `Common/ApiResponse.cs` | 🆕 | New generic response wrapper |
| **Datasets** |
| `Datasets/DatasetSummaryDto.cs` | `Datasets/DatasetSummaryDto.cs` | ✅ | Update namespace |
| `Datasets/DatasetDetailDto.cs` | `Datasets/DatasetDetailDto.cs` | ✅ | Update namespace |
| `Datasets/DatasetItemDto.cs` | `Datasets/DatasetItemDto.cs` | ✅ | Update namespace |
| `Datasets/CreateDatasetRequest.cs` | `Datasets/CreateDatasetRequest.cs` | ✅ | Update namespace |
| `Datasets/DatasetSourceType.cs` | `Datasets/DatasetSourceType.cs` | ✅ | Update namespace |
| `Datasets/IngestionStatusDto.cs` | `Datasets/IngestionStatusDto.cs` | ✅ | Update namespace |
| 🆕 | `Datasets/UpdateDatasetRequest.cs` | 🆕 | New DTO |
| 🆕 | `Datasets/ImportRequest.cs` | 🆕 | New DTO |
| **Items** |
| `Items/UpdateItemRequest.cs` | `Items/UpdateItemRequest.cs` | ✅ | Update namespace |
| **Users** |
| 🆕 | `Users/UserDto.cs` | 📝 | TODO Phase 2 |
| 🆕 | `Users/RegisterRequest.cs` | 📝 | TODO Phase 2 |
| 🆕 | `Users/LoginRequest.cs` | 📝 | TODO Phase 2 |
| 🆕 | `Users/UserSettingsDto.cs` | 📝 | TODO Phase 2 |
| **Extensions** |
| 🆕 | `Extensions/ExtensionInfoDto.cs` | 📝 | TODO Phase 3 |
| 🆕 | `Extensions/InstallExtensionRequest.cs` | 📝 | TODO Phase 3 |
| 🆕 | `Extensions/ExtensionSettingsDto.cs` | 📝 | TODO Phase 3 |
| **AI** |
| 🆕 | `AI/CaptionRequest.cs` | 📝 | TODO Phase 5 |
| 🆕 | `AI/CaptionResponse.cs` | 📝 | TODO Phase 5 |
| 🆕 | `AI/CaptionScore.cs` | 📝 | TODO Phase 5 |

---

## APIBackend Project Migration

### Source: `src/HartsysDatasetEditor.Api/` → Target: `src/APIBackend/`

| Old Path | New Path | Status | Notes |
|----------|----------|--------|-------|
| **Configuration** |
| `Program.cs` | `Configuration/Program.cs` | ✅ | Update namespace, update service registrations |
| `appsettings.json` | `Configuration/appsettings.json` | ✅ | Update paths |
| `appsettings.Development.json` | `Configuration/appsettings.Development.json` | ✅ | Update paths |
| **Controllers** |
| 🆕 | `Controllers/DatasetsController.cs` | 🆕 | Migrate from endpoints |
| 🆕 | `Controllers/ItemsController.cs` | 🆕 | Migrate from ItemEditEndpoints.cs |
| 🆕 | `Controllers/UsersController.cs` | 📝 | TODO Phase 2 |
| 🆕 | `Controllers/ExtensionsController.cs` | 📝 | TODO Phase 3 |
| 🆕 | `Controllers/AIController.cs` | 📝 | TODO Phase 5 |
| 🆕 | `Controllers/AdminController.cs` | 📝 | TODO Phase 2 |
| **Services** |
| `Services/IDatasetIngestionService.cs` | `Services/DatasetManagement/IDatasetIngestionService.cs` | ✅ | Update namespace |
| `Services/DatasetDiskImportService.cs` | `Services/DatasetManagement/DatasetDiskImportService.cs` | ✅ | Update namespace |
| `Services/HuggingFaceStreamingStrategy.cs` | `Services/DatasetManagement/HuggingFaceStreamingStrategy.cs` | ✅ | Update namespace |
| `Services/HuggingFaceDatasetServerClient.cs` | `Services/Integration/HuggingFaceDatasetServerClient.cs` | ✅ | Update namespace |
| `Services/HuggingFaceDiscoveryService.cs` | `Services/Integration/HuggingFaceDiscoveryService.cs` | ✅ | Update namespace |
| `Services/IHuggingFaceClient.cs` | `Services/Integration/IHuggingFaceClient.cs` | ✅ | Update namespace |
| `Services/Dtos/DatasetMappings.cs` | `Services/Dtos/DatasetMappings.cs` | ✅ | Update namespace |
| 🆕 | `Services/DatasetManagement/DatasetService.cs` | 🆕 | New service |
| 🆕 | `Services/DatasetManagement/IngestionService.cs` | 🆕 | New unified service |
| 🆕 | `Services/DatasetManagement/ParquetDataService.cs` | 📝 | TODO Phase 2 |
| 🆕 | `Services/Caching/CachingService.cs` | 📝 | TODO Phase 4 |
| 🆕 | `Services/Authentication/UserService.cs` | 📝 | TODO Phase 2 |
| 🆕 | `Services/Authentication/AuthService.cs` | 📝 | TODO Phase 2 |
| 🆕 | `Services/Extensions/ExtensionLoaderService.cs` | 📝 | TODO Phase 3 |
| 🆕 | `Services/Extensions/ExtensionHostService.cs` | 📝 | TODO Phase 3 |
| **DataAccess** |
| `Repositories/LiteDbDatasetEntityRepository.cs` | `DataAccess/LiteDB/Repositories/DatasetRepository.cs` | ✅ | Update namespace, rename |
| `Repositories/LiteDbDatasetItemRepository.cs` | `DataAccess/LiteDB/Repositories/ItemRepository.cs` | ✅ | Update namespace, rename |
| `Services/IDatasetRepository.cs` | _(move to Core/Abstractions)_ | ✅ | Already in Core |
| `Services/IDatasetItemRepository.cs` | _(move to Core/Abstractions)_ | ✅ | Already in Core |
| 🆕 | `DataAccess/PostgreSQL/DbContext.cs` | 📝 | TODO Phase 2 |
| 🆕 | `DataAccess/PostgreSQL/Repositories/DatasetRepository.cs` | 📝 | TODO Phase 2 |
| 🆕 | `DataAccess/PostgreSQL/Repositories/UserRepository.cs` | 📝 | TODO Phase 2 |
| 🆕 | `DataAccess/PostgreSQL/Repositories/ItemRepository.cs` | 📝 | TODO Phase 2 |
| 🆕 | `DataAccess/PostgreSQL/Migrations/` | 📝 | TODO Phase 2 |
| 🆕 | `DataAccess/Parquet/ParquetItemRepository.cs` | 📝 | TODO Phase 2 |
| 🆕 | `DataAccess/Parquet/ParquetWriter.cs` | 📝 | TODO Phase 2 |
| **Models** |
| `Models/DatasetEntity.cs` | `Models/DatasetEntity.cs` | ✅ | Update namespace |
| `Models/DatasetDiskMetadata.cs` | `Models/DatasetDiskMetadata.cs` | ✅ | Update namespace |
| `Models/HuggingFaceDatasetInfo.cs` | `Models/HuggingFaceDatasetInfo.cs` | ✅ | Update namespace |
| `Models/HuggingFaceDatasetProfile.cs` | `Models/HuggingFaceDatasetProfile.cs` | ✅ | Update namespace |
| **Endpoints** |
| `Endpoints/ItemEditEndpoints.cs` | _(migrate to Controllers/ItemsController.cs)_ | ✅ | Convert to controller |
| **Extensions** |
| `Extensions/ServiceCollectionExtensions.cs` | `Extensions/ServiceCollectionExtensions.cs` | ✅ | Update namespace |
| **Middleware** |
| 🆕 | `Middleware/AuthenticationMiddleware.cs` | 📝 | TODO Phase 2 |
| 🆕 | `Middleware/RateLimitingMiddleware.cs` | 📝 | TODO Phase 4 |
| 🆕 | `Middleware/ErrorHandlingMiddleware.cs` | 🆕 | Create now (basic) |
| **BackgroundWorkers** |
| 🆕 | `BackgroundWorkers/IngestionWorker.cs` | 📝 | TODO Phase 4 |
| 🆕 | `BackgroundWorkers/ThumbnailGenerationWorker.cs` | 📝 | TODO Phase 4 |
| 🆕 | `BackgroundWorkers/CacheWarmupWorker.cs` | 📝 | TODO Phase 4 |

---

## ClientApp Project Migration

### Source: `src/HartsysDatasetEditor.Client/` → Target: `src/ClientApp/`

| Old Path | New Path | Status | Notes |
|----------|----------|--------|-------|
| **Configuration** |
| `Program.cs` | `Configuration/Program.cs` | ✅ | Update namespace, service registrations |
| `App.razor` | `Configuration/App.razor` | ✅ | Update namespace |
| `_Imports.razor` | `Configuration/_Imports.razor` | ✅ | Update namespaces |
| **wwwroot** |
| `wwwroot/index.html` | `wwwroot/index.html` | ✅ | Update title |
| `wwwroot/css/app.css` | `wwwroot/css/app.css` | ✅ | Copy as-is |
| `wwwroot/js/*` | `wwwroot/js/*` | ✅ | Copy all JS files |
| 🆕 | `wwwroot/Themes/LightTheme.css` | 📝 | TODO Phase 4 |
| 🆕 | `wwwroot/Themes/DarkTheme.css` | 📝 | TODO Phase 4 |
| 🆕 | `wwwroot/Themes/CustomTheme.css` | 📝 | TODO Phase 4 |
| 🆕 | `wwwroot/js/Installer.js` | 📝 | TODO Phase 4 |
| **Features/Home** |
| `Pages/Index.razor` | `Features/Home/Pages/Index.razor` | ✅ | Update namespace |
| `Pages/Index.razor.cs` | `Features/Home/Pages/Index.razor.cs` | ✅ | Update namespace |
| 🆕 | `Features/Home/Components/WelcomeCard.razor` | 📝 | TODO Phase 4 |
| **Features/Installation** |
| 🆕 | `Features/Installation/Pages/Install.razor` | 📝 | TODO Phase 4 |
| 🆕 | `Features/Installation/Components/WelcomeStep.razor` | 📝 | TODO Phase 4 |
| 🆕 | `Features/Installation/Components/DeploymentModeStep.razor` | 📝 | TODO Phase 4 |
| 🆕 | `Features/Installation/Components/AdminAccountStep.razor` | 📝 | TODO Phase 4 |
| 🆕 | `Features/Installation/Components/ExtensionSelectionStep.razor` | 📝 | TODO Phase 4 |
| 🆕 | `Features/Installation/Components/StorageConfigStep.razor` | 📝 | TODO Phase 4 |
| 🆕 | `Features/Installation/Components/CompletionStep.razor` | 📝 | TODO Phase 4 |
| 🆕 | `Features/Installation/Services/InstallationService.cs` | 📝 | TODO Phase 4 |
| **Features/Datasets** |
| `Pages/MyDatasets.razor` | `Features/Datasets/Pages/DatasetLibrary.razor` | ✅ | Update namespace, rename |
| `Pages/MyDatasets.razor.cs` | `Features/Datasets/Pages/DatasetLibrary.razor.cs` | ✅ | Update namespace |
| `Pages/DatasetViewer.razor` | `Features/Datasets/Pages/DatasetViewer.razor` | ✅ | Update namespace |
| `Pages/DatasetViewer.razor.cs` | `Features/Datasets/Pages/DatasetViewer.razor.cs` | ✅ | Update namespace |
| `Pages/CreateDataset.razor` | `Features/Datasets/Pages/CreateDataset.razor` | ✅ | Update namespace |
| 🆕 | `Features/Datasets/Components/DatasetCard.razor` | 🆕 | Extract from library |
| `Components/Dataset/DatasetUploader.razor` | `Features/Datasets/Components/DatasetUploader.razor` | ✅ | Update namespace |
| `Components/Dataset/DatasetUploader.razor.cs` | `Features/Datasets/Components/DatasetUploader.razor.cs` | ✅ | Update namespace |
| `Components/Dataset/HuggingFaceDatasetOptions.razor` | `Features/Datasets/Components/HuggingFaceDatasetOptions.razor` | ✅ | Update namespace |
| `Components/Dataset/DatasetStats.razor` | `Features/Datasets/Components/DatasetStats.razor` | ✅ | Update namespace |
| `Components/Dataset/DatasetInfo.razor` | `Features/Datasets/Components/DatasetInfo.razor` | ✅ | Update namespace |
| `Components/Viewer/ImageGrid.razor` | `Features/Datasets/Components/ImageGrid.razor` | ✅ | Update namespace |
| `Components/Viewer/ImageGrid.razor.cs` | `Features/Datasets/Components/ImageGrid.razor.cs` | ✅ | Update namespace |
| `Components/Viewer/ImageCard.razor` | `Features/Datasets/Components/ImageCard.razor` | ✅ | Update namespace |
| `Components/Viewer/ImageCard.razor.cs` | `Features/Datasets/Components/ImageCard.razor.cs` | ✅ | Update namespace |
| `Components/Viewer/ImageList.razor` | `Features/Datasets/Components/ImageGallery.razor` | ✅ | Update namespace, rename |
| `Components/Viewer/ViewerContainer.razor` | `Features/Datasets/Components/ViewerContainer.razor` | ✅ | Update namespace |
| `Components/Viewer/ViewerContainer.razor.cs` | `Features/Datasets/Components/ViewerContainer.razor.cs` | ✅ | Update namespace |
| `Components/Viewer/ImageDetailPanel.razor` | `Features/Datasets/Components/ImageDetailPanel.razor` | ✅ | Update namespace |
| `Components/Viewer/ImageDetailPanel.razor.cs` | `Features/Datasets/Components/ImageDetailPanel.razor.cs` | ✅ | Update namespace |
| `Components/Viewer/ImageLightbox.razor` | `Features/Datasets/Components/ImageLightbox.razor` | ✅ | Update namespace |
| `Components/Filter/FilterPanel.razor` | `Features/Datasets/Components/FilterPanel.razor` | ✅ | Update namespace |
| `Components/Filter/FilterPanel.razor.cs` | `Features/Datasets/Components/FilterPanel.razor.cs` | ✅ | Update namespace |
| `Components/Filter/SearchBar.razor` | `Features/Datasets/Components/SearchBar.razor` | ✅ | Update namespace |
| `Components/Filter/FilterChips.razor` | `Features/Datasets/Components/FilterChips.razor` | ✅ | Update namespace |
| `Components/Filter/DateRangeFilter.razor` | `Features/Datasets/Components/DateRangeFilter.razor` | ✅ | Update namespace |
| 🆕 | `Features/Datasets/Components/InlineEditor.razor` | 📝 | TODO Phase 5 |
| 🆕 | `Features/Datasets/Components/AdvancedSearch.razor` | 📝 | TODO Phase 5 |
| `Services/DatasetCacheService.cs` | `Features/Datasets/Services/DatasetCacheService.cs` | ✅ | Update namespace |
| `Services/ItemEditService.cs` | `Features/Datasets/Services/ItemEditService.cs` | ✅ | Update namespace |
| **Features/Authentication** |
| 🆕 | `Features/Authentication/Pages/Login.razor` | 📝 | TODO Phase 2 |
| 🆕 | `Features/Authentication/Components/LoginForm.razor` | 📝 | TODO Phase 2 |
| 🆕 | `Features/Authentication/Components/RegisterForm.razor` | 📝 | TODO Phase 2 |
| **Features/Administration** |
| 🆕 | `Features/Administration/Pages/Admin.razor` | 📝 | TODO Phase 2 |
| 🆕 | `Features/Administration/Components/UserManagement.razor` | 📝 | TODO Phase 2 |
| 🆕 | `Features/Administration/Components/ExtensionManager.razor` | 📝 | TODO Phase 3 |
| 🆕 | `Features/Administration/Components/SystemSettings.razor` | 📝 | TODO Phase 2 |
| 🆕 | `Features/Administration/Components/Analytics.razor` | 📝 | TODO Phase 6 |
| **Features/Settings** |
| `Pages/Settings.razor` | `Features/Settings/Pages/Settings.razor` | ✅ | Update namespace |
| `Pages/AITools.razor` | _(remove for now)_ | ❌ | Will become extension |
| `Components/Settings/ThemeSelector.razor` | `Features/Settings/Components/ThemeSelector.razor` | ✅ | Update namespace |
| `Components/Settings/LanguageSelector.razor` | `Features/Settings/Components/LanguageSelector.razor` | ✅ | Update namespace |
| `Components/Settings/ViewPreferences.razor` | `Features/Settings/Components/ViewPreferences.razor` | ✅ | Update namespace |
| `Components/Settings/ApiKeySettingsPanel.razor` | `Features/Settings/Components/ApiKeySettingsPanel.razor` | ✅ | Update namespace |
| 🆕 | `Features/Settings/Components/AppearanceSettings.razor` | 🆕 | Extract from Settings |
| 🆕 | `Features/Settings/Components/AccountSettings.razor` | 📝 | TODO Phase 2 |
| 🆕 | `Features/Settings/Components/PrivacySettings.razor` | 📝 | TODO Phase 2 |
| **Shared** |
| `Layout/MainLayout.razor` | `Shared/Layout/MainLayout.razor` | ✅ | Update namespace |
| `Layout/MainLayout.razor.cs` | `Shared/Layout/MainLayout.razor.cs` | ✅ | Update namespace |
| `Layout/NavMenu.razor` | `Shared/Layout/NavMenu.razor` | ✅ | Update namespace |
| `Layout/NavMenu.razor.cs` | `Shared/Layout/NavMenu.razor.cs` | ✅ | Update namespace |
| 🆕 | `Shared/Layout/AdminLayout.razor` | 📝 | TODO Phase 2 |
| `Components/Common/LoadingIndicator.razor` | `Shared/Components/LoadingIndicator.razor` | ✅ | Update namespace |
| `Components/Common/EmptyState.razor` | `Shared/Components/EmptyState.razor` | ✅ | Update namespace |
| `Components/Common/ErrorBoundary.razor` | `Shared/Components/ErrorBoundary.razor` | ✅ | Update namespace |
| `Components/Common/ConfirmDialog.razor` | `Shared/Components/ConfirmDialog.razor` | ✅ | Update namespace |
| `Components/Common/DatasetSwitcher.razor` | `Shared/Components/DatasetSwitcher.razor` | ✅ | Update namespace |
| `Components/Common/LayoutSwitcher.razor` | `Shared/Components/LayoutSwitcher.razor` | ✅ | Update namespace |
| 🆕 | `Shared/Components/Toast.razor` | 🆕 | Integrate with NotificationService |
| `Services/NotificationService.cs` | `Shared/Services/NotificationService.cs` | ✅ | Update namespace |
| `Services/NavigationService.cs` | `Shared/Services/NavigationService.cs` | ✅ | Update namespace |
| 🆕 | `Shared/Services/ThemeService.cs` | 🆕 | Extract from AppState |
| **Services** |
| `Services/StateManagement/AppState.cs` | `Services/StateManagement/AppState.cs` | ✅ | Update namespace |
| `Services/StateManagement/DatasetState.cs` | `Services/StateManagement/DatasetState.cs` | ✅ | Update namespace |
| `Services/StateManagement/FilterState.cs` | `Services/StateManagement/FilterState.cs` | ✅ | Update namespace |
| `Services/StateManagement/ViewState.cs` | `Services/StateManagement/ViewState.cs` | ✅ | Update namespace |
| `Services/StateManagement/ApiKeyState.cs` | `Services/StateManagement/ApiKeyState.cs` | ✅ | Update namespace |
| 🆕 | `Services/StateManagement/UserState.cs` | 📝 | TODO Phase 2 |
| 🆕 | `Services/StateManagement/ExtensionState.cs` | 📝 | TODO Phase 3 |
| `Services/Api/DatasetApiClient.cs` | `Services/ApiClients/DatasetApiClient.cs` | ✅ | Update namespace |
| `Services/Api/DatasetApiOptions.cs` | `Services/ApiClients/DatasetApiOptions.cs` | ✅ | Update namespace |
| 🆕 | `Services/ApiClients/UserApiClient.cs` | 📝 | TODO Phase 2 |
| 🆕 | `Services/ApiClients/ExtensionApiClient.cs` | 📝 | TODO Phase 3 |
| 🆕 | `Services/ApiClients/AIApiClient.cs` | 📝 | TODO Phase 5 |
| `Services/DatasetIndexedDbCache.cs` | `Services/Caching/IndexedDbCache.cs` | ✅ | Update namespace, rename |
| 🆕 | `Services/Caching/ThumbnailCache.cs` | 📝 | TODO Phase 4 |
| `Services/JsInterop/IndexedDbInterop.cs` | `Services/Interop/IndexedDbInterop.cs` | ✅ | Update namespace |
| `Services/JsInterop/FileReaderInterop.cs` | `Services/Interop/FileReaderInterop.cs` | ✅ | Update namespace |
| `Services/JsInterop/ImageLazyLoadInterop.cs` | `Services/Interop/ImageLazyLoadInterop.cs` | ✅ | Update namespace |
| `Services/JsInterop/LocalStorageInterop.cs` | `Services/Interop/LocalStorageInterop.cs` | ✅ | Update namespace |
| 🆕 | `Services/Interop/InstallerInterop.cs` | 📝 | TODO Phase 4 |
| `Extensions/ServiceCollectionExtensions.cs` | `Extensions/ServiceCollectionExtensions.cs` | ✅ | Update namespace |
| `Components/Dialogs/AddTagDialog.razor` | _(move to Features/Datasets/Components)_ | ✅ | Update namespace |

---

## Extensions Scaffold (All TODO)

### Target: `src/Extensions/`

| Path | Status | Phase |
|------|--------|-------|
| `SDK/BaseExtension.cs` | 📝 | Phase 3 |
| `SDK/ExtensionMetadata.cs` | 📝 | Phase 3 |
| `SDK/ExtensionManifest.cs` | 📝 | Phase 3 |
| `SDK/DevelopmentGuide.md` | 📝 | Phase 3 |
| `BuiltIn/CoreViewer/extension.manifest.json` | 📝 | Phase 3 |
| `BuiltIn/CoreViewer/CoreViewerExtension.cs` | 📝 | Phase 3 |
| `BuiltIn/Creator/extension.manifest.json` | 📝 | Phase 3 |
| `BuiltIn/Creator/CreatorExtension.cs` | 📝 | Phase 3 |
| `BuiltIn/Editor/extension.manifest.json` | 📝 | Phase 5 |
| `BuiltIn/Editor/EditorExtension.cs` | 📝 | Phase 5 |
| `BuiltIn/AITools/extension.manifest.json` | 📝 | Phase 5 |
| `BuiltIn/AITools/AIToolsExtension.cs` | 📝 | Phase 5 |
| `BuiltIn/AdvancedTools/extension.manifest.json` | 📝 | Phase 6 |
| `BuiltIn/AdvancedTools/AdvancedToolsExtension.cs` | 📝 | Phase 6 |
| `UserExtensions/README.md` | 📝 | Phase 3 |

---

## Tests Migration

### Source: `tests/HartsysDatasetEditor.Tests/` → Target: `tests/DatasetStudio.Tests/`

| Old Path | New Path | Status |
|----------|----------|--------|
| `Api/ItemEditEndpointsTests.cs` | `APIBackend/Controllers/ItemsControllerTests.cs` | ✅ |
| `Client/ItemEditServiceTests.cs` | `ClientApp/Services/ItemEditServiceTests.cs` | ✅ |
| `Services/EnrichmentMergerServiceTests.cs` | `Core/Services/EnrichmentMergerServiceTests.cs` | ✅ |
| `Services/MultiFileDetectorServiceTests.cs` | `Core/Services/MultiFileDetectorServiceTests.cs` | ✅ |

---

## Documentation

### Target: `Docs/`

| Path | Status | Phase |
|------|--------|-------|
| `Installation/QuickStart.md` | 📝 | Phase 4 |
| `Installation/SingleUserSetup.md` | 📝 | Phase 4 |
| `Installation/MultiUserSetup.md` | 📝 | Phase 4 |
| `UserGuides/ViewingDatasets.md` | 📝 | Phase 4 |
| `UserGuides/CreatingDatasets.md` | 📝 | Phase 4 |
| `UserGuides/EditingDatasets.md` | 📝 | Phase 5 |
| `API/APIReference.md` | 📝 | Phase 6 |
| `Development/ExtensionDevelopment.md` | 📝 | Phase 3 |
| `Development/Contributing.md` | 📝 | Phase 6 |

---

## Summary Statistics

| Category | Migrate (✅) | Create New (🆕) | TODO (📝) | Delete (❌) |
|----------|-------------|----------------|-----------|------------|
| **Core** | 35 | 5 | 25 | 0 |
| **DTO** | 9 | 3 | 12 | 0 |
| **APIBackend** | 15 | 8 | 18 | 1 |
| **ClientApp** | 62 | 8 | 28 | 1 |
| **Extensions** | 0 | 0 | 15 | 0 |
| **Tests** | 4 | 0 | 0 | 0 |
| **Docs** | 0 | 0 | 9 | 0 |
| **TOTAL** | **125** | **24** | **107** | **2** |

---

*Last Updated: 2025-12-08*
*Total Files to Handle: 258*
