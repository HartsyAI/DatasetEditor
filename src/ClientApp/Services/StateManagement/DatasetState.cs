using DatasetStudio.Core.DomainModels;
using DatasetStudio.Core.DomainModels.Datasets;
using DatasetStudio.Core.Abstractions;
using DatasetStudio.Core.Utilities.Logging;
using DatasetStudio.DTO.Datasets;

namespace DatasetStudio.ClientApp.Services.StateManagement;

/// <summary>Manages the currently loaded dataset, items, and selection state.</summary>
public class DatasetState
{
    /// <summary>The currently loaded dataset, null if no dataset is loaded.</summary>
    public Dataset? CurrentDataset { get; private set; }
    
    /// <summary>All items in the current dataset.</summary>
    public List<DatasetItemDto> Items { get; private set; } = new();

    /// <summary>The currently selected single item for detail view.</summary>
    public DatasetItemDto? SelectedItem { get; private set; }

    /// <summary>Multiple selected items for bulk operations.</summary>
    public List<DatasetItemDto> SelectedItems { get; private set; } = new();
    
    /// <summary>Indicates whether a dataset is currently being loaded.</summary>
    public bool IsLoading { get; private set; }
    
    /// <summary>Error message if dataset loading failed.</summary>
    public string? ErrorMessage { get; private set; }
    
    /// <summary>Total count of items in the dataset.</summary>
    public int TotalCount => Items.Count;
    
    /// <summary>Count of currently selected items.</summary>
    public int SelectedCount => SelectedItems.Count;
    
    /// <summary>Indicates whether any items are selected.</summary>
    public bool HasSelection => SelectedItems.Count > 0;
    
    /// <summary>Event fired when any state property changes.</summary>
    public event Action? OnChange;

    /// <summary>
    /// Bumped each time <see cref="LoadDataset"/> replaces the items (opening a dataset, a filter
    /// change, a reload), but not when a page is appended. The grid resets its load-more tracking
    /// on a change: tracking by item count alone missed a new result set that happened to be as
    /// long as the old one.
    /// </summary>
    public int ResultSetVersion { get; private set; }

    /// <summary>
    /// Bumped each time <see cref="UpdateItem"/> replaces a loaded item. The grid re-renders only
    /// when the item count or result set changes, so without this an edited card kept showing its
    /// old values (and handed its old copy, with the old version, to the detail panel).
    /// </summary>
    public int ItemEditVersion { get; private set; }

    /// <summary>Loads a new dataset and its items, replacing any existing dataset.</summary>
    /// <param name="dataset">Dataset metadata to load.</param>
    /// <param name="items">List of dataset items.</param>
    public void LoadDataset(Dataset dataset, List<DatasetItemDto> items)
    {
        ResultSetVersion++;
        CurrentDataset = dataset;
        Items = items;
        SelectedItem = null;
        SelectedItems.Clear();
        ErrorMessage = null;
        IsLoading = false;
        NotifyStateChanged();
        Logs.Info($"Dataset loaded: {dataset.Name} with {items.Count} items");
    }

    public void SetItemsWindow(List<DatasetItemDto> items)
    {
        if (items is null)
        {
            Items.Clear();
        }
        else
        {
            Items.Clear();
            Items.AddRange(items);
        }

        NotifyStateChanged();
        Logs.Info($"Dataset window updated: {Items.Count} items");
    }
    
    /// <summary>Sets the loading state and clears any previous errors.</summary>
    /// <param name="isLoading">Whether dataset is currently loading.</param>
    public void SetLoading(bool isLoading)
    {
        IsLoading = isLoading;
        if (isLoading)
        {
            ErrorMessage = null;
        }
        NotifyStateChanged();
    }
    
    /// <summary>Sets an error message when dataset loading fails.</summary>
    /// <param name="errorMessage">Error message to display.</param>
    public void SetError(string errorMessage)
    {
        ErrorMessage = errorMessage;
        IsLoading = false;
        NotifyStateChanged();
        Logs.Error($"Dataset loading error: {errorMessage}");
    }
    
    /// <summary>Selects a single item for detail view, replacing any previous selection.</summary>
    /// <param name="item">Item to select.</param>
    /// <remarks>
    /// Takes the loaded copy with the same id when there is one. A card can hand over the copy it
    /// was last rendered with, from before an edit; the panel then showed the old values, and its
    /// next edit sent the old version and was refused as stale (audit E-4).
    /// </remarks>
    public void SelectItem(DatasetItemDto item)
    {
        SelectedItem = Items.FirstOrDefault(i => i.Id == item.Id) ?? item;
        NotifyStateChanged();
        Logs.Info($"Item selected: {item.Id}");
    }
    
    /// <summary>Toggles an item in the multi-selection list.</summary>
    /// <param name="item">Item to toggle selection for.</param>
    public void ToggleSelection(DatasetItemDto item)
    {
        if (SelectedItems.Contains(item))
        {
            SelectedItems.Remove(item);
            Logs.Info($"Item deselected: {item.Id}");
        }
        else
        {
            SelectedItems.Add(item);
            Logs.Info($"Item selected: {item.Id}");
        }
        NotifyStateChanged();
    }
    
    /// <summary>Checks if a specific item is currently selected.</summary>
    /// <param name="item">Item to check.</param>
    /// <returns>True if item is in the selection list.</returns>
    public bool IsSelected(DatasetItemDto item)
    {
        return SelectedItems.Contains(item);
    }
    
    /// <summary>Replaces an item in the dataset and in any selection that holds it.</summary>
    /// <param name="item">Edited item; matched to the existing entry by <see cref="DatasetItemDto.Id"/>.</param>
    /// <remarks>
    /// Selection holds the DTO that was selected, and DTO equality is by value, so a stale entry no
    /// longer matches the edited item. Without swapping the selection too, the checkbox unchecks and
    /// a second toggle adds a duplicate for the same id.
    /// </remarks>
    public void UpdateItem(DatasetItemDto item)
    {
        int index = Items.FindIndex(i => i.Id == item.Id);
        if (index >= 0)
        {
            Items[index] = item;
            for (int i = 0; i < SelectedItems.Count; i++)
            {
                if (SelectedItems[i].Id == item.Id)
                {
                    SelectedItems[i] = item;
                }
            }
            if (SelectedItem?.Id == item.Id)
            {
                SelectedItem = item;
            }
            ItemEditVersion++;
            NotifyStateChanged();
            Logs.Info($"Item updated: {item.Id}");
        }
    }
    
    /// <summary>Notifies all subscribers that the state has changed.</summary>
    protected void NotifyStateChanged()
    {
        OnChange?.Invoke();
    }
    
    // TODO: Add method to add new items to dataset
    // TODO: Add method to remove items from dataset
    // TODO: Add method to update item metadata
    // TODO: Add favorites/bookmarks functionality
}
