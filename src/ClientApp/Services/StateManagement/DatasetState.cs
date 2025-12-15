using DatasetStudio.Core.DomainModels;
using DatasetStudio.Core.DomainModels.Datasets;
using DatasetStudio.Core.Abstractions;
using DatasetStudio.Core.Utilities;
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
    
    /// <summary>Loads a new dataset and its items, replacing any existing dataset.</summary>
    /// <param name="dataset">Dataset metadata to load.</param>
    /// <param name="items">List of dataset items.</param>
    public void LoadDataset(Dataset dataset, List<DatasetItemDto> items)
    {
        CurrentDataset = dataset;
        Items = items;
        SelectedItem = null;
        SelectedItems.Clear();
        ErrorMessage = null;
        IsLoading = false;
        NotifyStateChanged();
        Logs.Info($"Dataset loaded: {dataset.Name} with {items.Count} items");
    }

    /// <summary>Appends additional items to the current dataset (e.g., next API page).</summary>
    /// <param name="items">Items to append.</param>
    public void AppendItems(IEnumerable<DatasetItemDto> items)
    {
        if (items == null)
        {
            return;
        }

        int beforeCount = Items.Count;
        Items.AddRange(items);
        if (Items.Count != beforeCount)
        {
            NotifyStateChanged();
            Logs.Info($"Appended {Items.Count - beforeCount} new items (total {Items.Count})");
        }
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
    public void SelectItem(DatasetItemDto item)
    {
        SelectedItem = item;
        NotifyStateChanged();
        Logs.Info($"Item selected: {item.Id}");
    }
    
    /// <summary>Clears the single item selection.</summary>
    public void ClearSelectedItem()
    {
        SelectedItem = null;
        NotifyStateChanged();
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
    
    /// <summary>Adds an item to the multi-selection list if not already selected.</summary>
    /// <param name="item">Item to add to selection.</param>
    public void AddToSelection(DatasetItemDto item)
    {
        if (!SelectedItems.Contains(item))
        {
            SelectedItems.Add(item);
            NotifyStateChanged();
            Logs.Info($"Item added to selection: {item.Id}");
        }
    }
    
    /// <summary>Removes an item from the multi-selection list.</summary>
    /// <param name="item">Item to remove from selection.</param>
    public void RemoveFromSelection(DatasetItemDto item)
    {
        if (SelectedItems.Remove(item))
        {
            NotifyStateChanged();
            Logs.Info($"Item removed from selection: {item.Id}");
        }
    }
    
    /// <summary>Clears all multi-selected items.</summary>
    public void ClearSelection()
    {
        SelectedItems.Clear();
        NotifyStateChanged();
        Logs.Info("Selection cleared");
    }
    
    /// <summary>Selects all items in the current dataset.</summary>
    public void SelectAll()
    {
        SelectedItems = new List<DatasetItemDto>(Items);
        NotifyStateChanged();
        Logs.Info($"All {Items.Count} items selected");
    }
    
    /// <summary>Checks if a specific item is currently selected.</summary>
    /// <param name="item">Item to check.</param>
    /// <returns>True if item is in the selection list.</returns>
    public bool IsSelected(DatasetItemDto item)
    {
        return SelectedItems.Contains(item);
    }
    
    /// <summary>Updates an item in the dataset.</summary>
    /// <param name="item">Item to update.</param>
    public void UpdateItem(DatasetItemDto item)
    {
        int index = Items.FindIndex(i => i.Id == item.Id);
        if (index >= 0)
        {
            Items[index] = item;
            NotifyStateChanged();
            Logs.Info($"Item updated: {item.Id}");
        }
    }
    
    /// <summary>Clears the current dataset and resets all state.</summary>
    public void ClearDataset()
    {
        CurrentDataset = null;
        Items.Clear();
        SelectedItem = null;
        SelectedItems.Clear();
        ErrorMessage = null;
        IsLoading = false;
        NotifyStateChanged();
        Logs.Info("Dataset cleared");
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
