#!/usr/bin/env python3
"""
Migration script to copy files from HartsysDatasetEditor.Client to ClientApp
and update all namespaces and using statements.
"""

import os
import re
import shutil
from pathlib import Path

# Source and destination base paths
SRC_BASE = r"c:\Users\kaleb\OneDrive\Desktop\Projects\DatasetEditor\src\HartsysDatasetEditor.Client"
DEST_BASE = r"c:\Users\kaleb\OneDrive\Desktop\Projects\DatasetEditor\src\ClientApp"

# File mapping: (source_relative_path, dest_relative_path)
FILE_MAPPINGS = [
    # Pages - Datasets
    ("Pages/MyDatasets.razor.cs", "Features/Datasets/Pages/DatasetLibrary.razor.cs"),
    ("Pages/DatasetViewer.razor", "Features/Datasets/Pages/DatasetViewer.razor"),
    ("Pages/DatasetViewer.razor.cs", "Features/Datasets/Pages/DatasetViewer.razor.cs"),
    ("Pages/CreateDataset.razor", "Features/Datasets/Pages/CreateDataset.razor"),
    ("Pages/AITools.razor", "Features/Datasets/Pages/AITools.razor"),

    # Pages - Settings
    ("Pages/Settings.razor", "Features/Settings/Pages/Settings.razor"),

    # Components - Dataset
    ("Components/Dataset/DatasetInfo.razor", "Features/Datasets/Components/DatasetInfo.razor"),
    ("Components/Dataset/DatasetStats.razor", "Features/Datasets/Components/DatasetStats.razor"),
    ("Components/Dataset/DatasetUploader.razor", "Features/Datasets/Components/DatasetUploader.razor"),
    ("Components/Dataset/DatasetUploader.razor.cs", "Features/Datasets/Components/DatasetUploader.razor.cs"),
    ("Components/Dataset/HuggingFaceDatasetOptions.razor", "Features/Datasets/Components/HuggingFaceDatasetOptions.razor"),

    # Components - Viewer
    ("Components/Viewer/ImageCard.razor", "Features/Datasets/Components/ImageCard.razor"),
    ("Components/Viewer/ImageCard.razor.cs", "Features/Datasets/Components/ImageCard.razor.cs"),
    ("Components/Viewer/ImageDetailPanel.razor", "Features/Datasets/Components/ImageDetailPanel.razor"),
    ("Components/Viewer/ImageDetailPanel.razor.cs", "Features/Datasets/Components/ImageDetailPanel.razor.cs"),
    ("Components/Viewer/ImageGrid.razor", "Features/Datasets/Components/ImageGrid.razor"),
    ("Components/Viewer/ImageGrid.razor.cs", "Features/Datasets/Components/ImageGrid.razor.cs"),
    ("Components/Viewer/ImageList.razor", "Features/Datasets/Components/ImageList.razor"),
    ("Components/Viewer/ImageLightbox.razor", "Features/Datasets/Components/ImageLightbox.razor"),
    ("Components/Viewer/ViewerContainer.razor", "Features/Datasets/Components/ViewerContainer.razor"),
    ("Components/Viewer/ViewerContainer.razor.cs", "Features/Datasets/Components/ViewerContainer.razor.cs"),

    # Components - Filter
    ("Components/Filter/FilterPanel.razor", "Features/Datasets/Components/FilterPanel.razor"),
    ("Components/Filter/FilterPanel.razor.cs", "Features/Datasets/Components/FilterPanel.razor.cs"),
    ("Components/Filter/DateRangeFilter.razor", "Features/Datasets/Components/DateRangeFilter.razor"),
    ("Components/Filter/FilterChips.razor", "Features/Datasets/Components/FilterChips.razor"),
    ("Components/Filter/SearchBar.razor", "Features/Datasets/Components/SearchBar.razor"),

    # Components - Dialogs
    ("Components/Dialogs/AddTagDialog.razor", "Features/Datasets/Components/AddTagDialog.razor"),

    # Components - Settings
    ("Components/Settings/ApiKeySettingsPanel.razor", "Features/Settings/Components/ApiKeySettingsPanel.razor"),
    ("Components/Settings/LanguageSelector.razor", "Features/Settings/Components/LanguageSelector.razor"),
    ("Components/Settings/ThemeSelector.razor", "Features/Settings/Components/ThemeSelector.razor"),
    ("Components/Settings/ViewPreferences.razor", "Features/Settings/Components/ViewPreferences.razor"),

    # Components - Common -> Shared
    ("Components/Common/ConfirmDialog.razor", "Shared/Components/ConfirmDialog.razor"),
    ("Components/Common/DatasetSwitcher.razor", "Shared/Components/DatasetSwitcher.razor"),
    ("Components/Common/EmptyState.razor", "Shared/Components/EmptyState.razor"),
    ("Components/Common/ErrorBoundary.razor", "Shared/Components/ErrorBoundary.razor"),
    ("Components/Common/LayoutSwitcher.razor", "Shared/Components/LayoutSwitcher.razor"),
    ("Components/Common/LoadingIndicator.razor", "Shared/Components/LoadingIndicator.razor"),

    # Layout
    ("Layout/MainLayout.razor", "Shared/Layout/MainLayout.razor"),
    ("Layout/MainLayout.razor.cs", "Shared/Layout/MainLayout.razor.cs"),
    ("Layout/NavMenu.razor", "Shared/Layout/NavMenu.razor"),
    ("Layout/NavMenu.razor.cs", "Shared/Layout/NavMenu.razor.cs"),

    # Services
    ("Services/Api/DatasetApiClient.cs", "Services/ApiClients/DatasetApiClient.cs"),
    ("Services/Api/DatasetApiOptions.cs", "Services/ApiClients/DatasetApiOptions.cs"),
    ("Services/DatasetIndexedDbCache.cs", "Services/Caching/IndexedDbCache.cs"),
    ("Services/DatasetCacheService.cs", "Features/Datasets/Services/DatasetCacheService.cs"),
    ("Services/ItemEditService.cs", "Features/Datasets/Services/ItemEditService.cs"),
    ("Services/ImageUrlHelper.cs", "Features/Datasets/Services/ImageUrlHelper.cs"),
    ("Services/JsInterop/FileReaderInterop.cs", "Services/Interop/FileReaderInterop.cs"),
    ("Services/JsInterop/ImageLazyLoadInterop.cs", "Services/Interop/ImageLazyLoadInterop.cs"),
    ("Services/JsInterop/IndexedDbInterop.cs", "Services/Interop/IndexedDbInterop.cs"),
    ("Services/JsInterop/LocalStorageInterop.cs", "Services/Interop/LocalStorageInterop.cs"),
    ("Services/NotificationService.cs", "Shared/Services/NotificationService.cs"),
    ("Services/NavigationService.cs", "Shared/Services/NavigationService.cs"),
    ("Services/StateManagement/ApiKeyState.cs", "Services/StateManagement/ApiKeyState.cs"),
    ("Services/StateManagement/AppState.cs", "Services/StateManagement/AppState.cs"),
    ("Services/StateManagement/DatasetState.cs", "Services/StateManagement/DatasetState.cs"),
    ("Services/StateManagement/FilterState.cs", "Services/StateManagement/FilterState.cs"),
    ("Services/StateManagement/ViewState.cs", "Services/StateManagement/ViewState.cs"),

    # Extensions
    ("Extensions/ServiceCollectionExtensions.cs", "Extensions/ServiceCollectionExtensions.cs"),
]

# Namespace mappings: (old_namespace_pattern, new_namespace)
NAMESPACE_REPLACEMENTS = [
    (r"HartsysDatasetEditor\.Client\.Pages", "DatasetStudio.ClientApp.Features.Datasets.Pages"),
    (r"HartsysDatasetEditor\.Client\.Components\.Dataset", "DatasetStudio.ClientApp.Features.Datasets.Components"),
    (r"HartsysDatasetEditor\.Client\.Components\.Viewer", "DatasetStudio.ClientApp.Features.Datasets.Components"),
    (r"HartsysDatasetEditor\.Client\.Components\.Filter", "DatasetStudio.ClientApp.Features.Datasets.Components"),
    (r"HartsysDatasetEditor\.Client\.Components\.Dialogs", "DatasetStudio.ClientApp.Features.Datasets.Components"),
    (r"HartsysDatasetEditor\.Client\.Components\.Settings", "DatasetStudio.ClientApp.Features.Settings.Components"),
    (r"HartsysDatasetEditor\.Client\.Components\.Common", "DatasetStudio.ClientApp.Shared.Components"),
    (r"HartsysDatasetEditor\.Client\.Layout", "DatasetStudio.ClientApp.Shared.Layout"),
    (r"HartsysDatasetEditor\.Client\.Services\.Api", "DatasetStudio.ClientApp.Services.ApiClients"),
    (r"HartsysDatasetEditor\.Client\.Services\.JsInterop", "DatasetStudio.ClientApp.Services.Interop"),
    (r"HartsysDatasetEditor\.Client\.Services\.StateManagement", "DatasetStudio.ClientApp.Services.StateManagement"),
    (r"HartsysDatasetEditor\.Client\.Services", "DatasetStudio.ClientApp.Features.Datasets.Services"),
    (r"HartsysDatasetEditor\.Client\.Extensions", "DatasetStudio.ClientApp.Extensions"),
    (r"HartsysDatasetEditor\.Client", "DatasetStudio.ClientApp"),
    (r"HartsysDatasetEditor\.Core\.Models", "DatasetStudio.Core.DomainModels"),
    (r"HartsysDatasetEditor\.Core\.Enums", "DatasetStudio.Core.Enumerations"),
    (r"HartsysDatasetEditor\.Core\.Interfaces", "DatasetStudio.Core.Abstractions"),
    (r"HartsysDatasetEditor\.Core\.Services", "DatasetStudio.Core.BusinessLogic"),
    (r"HartsysDatasetEditor\.Core\.Services\.Layouts", "DatasetStudio.Core.BusinessLogic.Layouts"),
    (r"HartsysDatasetEditor\.Core\.Services\.Parsers", "DatasetStudio.Core.BusinessLogic.Parsers"),
    (r"HartsysDatasetEditor\.Core\.Services\.Providers", "DatasetStudio.Core.BusinessLogic.Modality"),
    (r"HartsysDatasetEditor\.Core", "DatasetStudio.Core"),
    (r"HartsysDatasetEditor\.Contracts", "DatasetStudio.DTO"),
]

def update_content(content):
    """Update namespaces and using statements in file content."""
    for old_pattern, new_namespace in NAMESPACE_REPLACEMENTS:
        content = re.sub(old_pattern, new_namespace, content)
    return content

def migrate_file(src_rel, dest_rel):
    """Migrate a single file from source to destination."""
    src_path = os.path.join(SRC_BASE, src_rel)
    dest_path = os.path.join(DEST_BASE, dest_rel)

    if not os.path.exists(src_path):
        print(f"  [SKIP] Source not found: {src_rel}")
        return False

    # Create destination directory if it doesn't exist
    dest_dir = os.path.dirname(dest_path)
    os.makedirs(dest_dir, exist_ok=True)

    # Read source file
    try:
        with open(src_path, 'r', encoding='utf-8') as f:
            content = f.read()
    except Exception as e:
        print(f"  [ERROR] Failed to read {src_rel}: {e}")
        return False

    # Update namespaces
    updated_content = update_content(content)

    # Write to destination
    try:
        with open(dest_path, 'w', encoding='utf-8') as f:
            f.write(updated_content)
        print(f"  [OK] {src_rel} -> {dest_rel}")
        return True
    except Exception as e:
        print(f"  [ERROR] Failed to write {dest_rel}: {e}")
        return False

def main():
    """Main migration function."""
    print("Starting ClientApp migration...")
    print(f"Source: {SRC_BASE}")
    print(f"Destination: {DEST_BASE}")
    print(f"Files to migrate: {len(FILE_MAPPINGS)}")
    print()

    success_count = 0
    fail_count = 0

    for src_rel, dest_rel in FILE_MAPPINGS:
        if migrate_file(src_rel, dest_rel):
            success_count += 1
        else:
            fail_count += 1

    print()
    print(f"Migration complete: {success_count} succeeded, {fail_count} failed")

if __name__ == "__main__":
    main()
