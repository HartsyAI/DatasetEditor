#!/bin/bash

SRC="c:/Users/kaleb/OneDrive/Desktop/Projects/DatasetEditor/src/HartsysDatasetEditor.Client"
DEST="c:/Users/kaleb/OneDrive/Desktop/Projects/DatasetEditor/src/ClientApp"

echo "Migrating ClientApp files..."

# Function to copy and update a file
migrate_file() {
    local src_rel="$1"
    local dest_rel="$2"
    local src_path="$SRC/$src_rel"
    local dest_path="$DEST/$dest_rel"

    if [ ! -f "$src_path" ]; then
        echo "  [SKIP] $src_rel (not found)"
        return 1
    fi

    # Create destination directory
    mkdir -p "$(dirname "$dest_path")"

    # Copy and update namespaces using sed
    sed -e 's/HartsysDatasetEditor\.Client\.Pages/DatasetStudio.ClientApp.Features.Datasets.Pages/g' \
        -e 's/HartsysDatasetEditor\.Client\.Components\.Dataset/DatasetStudio.ClientApp.Features.Datasets.Components/g' \
        -e 's/HartsysDatasetEditor\.Client\.Components\.Viewer/DatasetStudio.ClientApp.Features.Datasets.Components/g' \
        -e 's/HartsysDatasetEditor\.Client\.Components\.Filter/DatasetStudio.ClientApp.Features.Datasets.Components/g' \
        -e 's/HartsysDatasetEditor\.Client\.Components\.Dialogs/DatasetStudio.ClientApp.Features.Datasets.Components/g' \
        -e 's/HartsysDatasetEditor\.Client\.Components\.Settings/DatasetStudio.ClientApp.Features.Settings.Components/g' \
        -e 's/HartsysDatasetEditor\.Client\.Components\.Common/DatasetStudio.ClientApp.Shared.Components/g' \
        -e 's/HartsysDatasetEditor\.Client\.Layout/DatasetStudio.ClientApp.Shared.Layout/g' \
        -e 's/HartsysDatasetEditor\.Client\.Services\.Api/DatasetStudio.ClientApp.Services.ApiClients/g' \
        -e 's/HartsysDatasetEditor\.Client\.Services\.JsInterop/DatasetStudio.ClientApp.Services.Interop/g' \
        -e 's/HartsysDatasetEditor\.Client\.Services\.StateManagement/DatasetStudio.ClientApp.Services.StateManagement/g' \
        -e 's/HartsysDatasetEditor\.Client\.Services/DatasetStudio.ClientApp.Features.Datasets.Services/g' \
        -e 's/HartsysDatasetEditor\.Client\.Extensions/DatasetStudio.ClientApp.Extensions/g' \
        -e 's/HartsysDatasetEditor\.Client/DatasetStudio.ClientApp/g' \
        -e 's/HartsysDatasetEditor\.Core\.Models/DatasetStudio.Core.DomainModels/g' \
        -e 's/HartsysDatasetEditor\.Core\.Enums/DatasetStudio.Core.Enumerations/g' \
        -e 's/HartsysDatasetEditor\.Core\.Interfaces/DatasetStudio.Core.Abstractions/g' \
        -e 's/HartsysDatasetEditor\.Core\.Services\.Layouts/DatasetStudio.Core.BusinessLogic.Layouts/g' \
        -e 's/HartsysDatasetEditor\.Core\.Services\.Parsers/DatasetStudio.Core.BusinessLogic.Parsers/g' \
        -e 's/HartsysDatasetEditor\.Core\.Services\.Providers/DatasetStudio.Core.BusinessLogic.Modality/g' \
        -e 's/HartsysDatasetEditor\.Core\.Services/DatasetStudio.Core.BusinessLogic/g' \
        -e 's/HartsysDatasetEditor\.Core/DatasetStudio.Core/g' \
        -e 's/HartsysDatasetEditor\.Contracts/DatasetStudio.DTO/g' \
        "$src_path" > "$dest_path"

    echo "  [OK] $src_rel -> $dest_rel"
    return 0
}

# Migrate all files
migrate_file "Pages/MyDatasets.razor.cs" "Features/Datasets/Pages/DatasetLibrary.razor.cs"
migrate_file "Pages/DatasetViewer.razor" "Features/Datasets/Pages/DatasetViewer.razor"
migrate_file "Pages/DatasetViewer.razor.cs" "Features/Datasets/Pages/DatasetViewer.razor.cs"
migrate_file "Pages/CreateDataset.razor" "Features/Datasets/Pages/CreateDataset.razor"
migrate_file "Pages/AITools.razor" "Features/Datasets/Pages/AITools.razor"
migrate_file "Pages/Settings.razor" "Features/Settings/Pages/Settings.razor"

# Components - Dataset
migrate_file "Components/Dataset/DatasetInfo.razor" "Features/Datasets/Components/DatasetInfo.razor"
migrate_file "Components/Dataset/DatasetStats.razor" "Features/Datasets/Components/DatasetStats.razor"
migrate_file "Components/Dataset/DatasetUploader.razor" "Features/Datasets/Components/DatasetUploader.razor"
migrate_file "Components/Dataset/DatasetUploader.razor.cs" "Features/Datasets/Components/DatasetUploader.razor.cs"
migrate_file "Components/Dataset/HuggingFaceDatasetOptions.razor" "Features/Datasets/Components/HuggingFaceDatasetOptions.razor"

# Components - Viewer
migrate_file "Components/Viewer/ImageCard.razor" "Features/Datasets/Components/ImageCard.razor"
migrate_file "Components/Viewer/ImageCard.razor.cs" "Features/Datasets/Components/ImageCard.razor.cs"
migrate_file "Components/Viewer/ImageDetailPanel.razor" "Features/Datasets/Components/ImageDetailPanel.razor"
migrate_file "Components/Viewer/ImageDetailPanel.razor.cs" "Features/Datasets/Components/ImageDetailPanel.razor.cs"
migrate_file "Components/Viewer/ImageGrid.razor" "Features/Datasets/Components/ImageGrid.razor"
migrate_file "Components/Viewer/ImageGrid.razor.cs" "Features/Datasets/Components/ImageGrid.razor.cs"
migrate_file "Components/Viewer/ImageList.razor" "Features/Datasets/Components/ImageList.razor"
migrate_file "Components/Viewer/ImageLightbox.razor" "Features/Datasets/Components/ImageLightbox.razor"
migrate_file "Components/Viewer/ViewerContainer.razor" "Features/Datasets/Components/ViewerContainer.razor"
migrate_file "Components/Viewer/ViewerContainer.razor.cs" "Features/Datasets/Components/ViewerContainer.razor.cs"

# Components - Filter
migrate_file "Components/Filter/FilterPanel.razor" "Features/Datasets/Components/FilterPanel.razor"
migrate_file "Components/Filter/FilterPanel.razor.cs" "Features/Datasets/Components/FilterPanel.razor.cs"
migrate_file "Components/Filter/DateRangeFilter.razor" "Features/Datasets/Components/DateRangeFilter.razor"
migrate_file "Components/Filter/FilterChips.razor" "Features/Datasets/Components/FilterChips.razor"
migrate_file "Components/Filter/SearchBar.razor" "Features/Datasets/Components/SearchBar.razor"

# Components - Dialogs
migrate_file "Components/Dialogs/AddTagDialog.razor" "Features/Datasets/Components/AddTagDialog.razor"

# Components - Settings
migrate_file "Components/Settings/ApiKeySettingsPanel.razor" "Features/Settings/Components/ApiKeySettingsPanel.razor"
migrate_file "Components/Settings/LanguageSelector.razor" "Features/Settings/Components/LanguageSelector.razor"
migrate_file "Components/Settings/ThemeSelector.razor" "Features/Settings/Components/ThemeSelector.razor"
migrate_file "Components/Settings/ViewPreferences.razor" "Features/Settings/Components/ViewPreferences.razor"

# Components - Common
migrate_file "Components/Common/ConfirmDialog.razor" "Shared/Components/ConfirmDialog.razor"
migrate_file "Components/Common/DatasetSwitcher.razor" "Shared/Components/DatasetSwitcher.razor"
migrate_file "Components/Common/EmptyState.razor" "Shared/Components/EmptyState.razor"
migrate_file "Components/Common/ErrorBoundary.razor" "Shared/Components/ErrorBoundary.razor"
migrate_file "Components/Common/LayoutSwitcher.razor" "Shared/Components/LayoutSwitcher.razor"
migrate_file "Components/Common/LoadingIndicator.razor" "Shared/Components/LoadingIndicator.razor"

# Layout
migrate_file "Layout/MainLayout.razor" "Shared/Layout/MainLayout.razor"
migrate_file "Layout/MainLayout.razor.cs" "Shared/Layout/MainLayout.razor.cs"
migrate_file "Layout/NavMenu.razor" "Shared/Layout/NavMenu.razor"
migrate_file "Layout/NavMenu.razor.cs" "Shared/Layout/NavMenu.razor.cs"

# Services
migrate_file "Services/Api/DatasetApiClient.cs" "Services/ApiClients/DatasetApiClient.cs"
migrate_file "Services/Api/DatasetApiOptions.cs" "Services/ApiClients/DatasetApiOptions.cs"
migrate_file "Services/DatasetIndexedDbCache.cs" "Services/Caching/IndexedDbCache.cs"
migrate_file "Services/DatasetCacheService.cs" "Features/Datasets/Services/DatasetCacheService.cs"
migrate_file "Services/ItemEditService.cs" "Features/Datasets/Services/ItemEditService.cs"
migrate_file "Services/ImageUrlHelper.cs" "Features/Datasets/Services/ImageUrlHelper.cs"
migrate_file "Services/JsInterop/FileReaderInterop.cs" "Services/Interop/FileReaderInterop.cs"
migrate_file "Services/JsInterop/ImageLazyLoadInterop.cs" "Services/Interop/ImageLazyLoadInterop.cs"
migrate_file "Services/JsInterop/IndexedDbInterop.cs" "Services/Interop/IndexedDbInterop.cs"
migrate_file "Services/JsInterop/LocalStorageInterop.cs" "Services/Interop/LocalStorageInterop.cs"
migrate_file "Services/NotificationService.cs" "Shared/Services/NotificationService.cs"
migrate_file "Services/NavigationService.cs" "Shared/Services/NavigationService.cs"
migrate_file "Services/StateManagement/ApiKeyState.cs" "Services/StateManagement/ApiKeyState.cs"
migrate_file "Services/StateManagement/AppState.cs" "Services/StateManagement/AppState.cs"
migrate_file "Services/StateManagement/DatasetState.cs" "Services/StateManagement/DatasetState.cs"
migrate_file "Services/StateManagement/FilterState.cs" "Services/StateManagement/FilterState.cs"
migrate_file "Services/StateManagement/ViewState.cs" "Services/StateManagement/ViewState.cs"

# Extensions
migrate_file "Extensions/ServiceCollectionExtensions.cs" "Extensions/ServiceCollectionExtensions.cs"

echo ""
echo "Migration complete!"
