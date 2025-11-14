using HartsysDatasetEditor.Core.Models;
using HartsysDatasetEditor.Core.Interfaces;
using HartsysDatasetEditor.Core.Utilities;

namespace HartsysDatasetEditor.Client.Services.StateManagement;

/// <summary>Manages the currently loaded dataset, items, and selection state.</summary>
public class DatasetState
{
    /// <summary>The currently loaded dataset, null if no dataset is loaded.</summary>
    public Dataset? CurrentDataset { get; private set; }
    
    /// <summary>All items in the current dataset.</summary>
    public List<IDatasetItem> Items { get; private set; } = new();
    
    /// <summary>The currently selected single item for detail view.</summary>
    public IDatasetItem? SelectedItem { get; private set; }
    
    /// <summary>Multiple selected items for bulk operations.</summary>
    public List<IDatasetItem> SelectedItems { get; private set; } = new();
    
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
    public void LoadDataset(Dataset dataset, List<IDatasetItem> items)
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
    public void SelectItem(IDatasetItem item)
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
    public void ToggleSelection(IDatasetItem item)
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
    public void AddToSelection(IDatasetItem item)
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
    public void RemoveFromSelection(IDatasetItem item)
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
        SelectedItems = new List<IDatasetItem>(Items);
        NotifyStateChanged();
        Logs.Info($"All {Items.Count} items selected");
    }
    
    /// <summary>Checks if a specific item is currently selected.</summary>
    /// <param name="item">Item to check.</param>
    /// <returns>True if item is in the selection list.</returns>
    public bool IsSelected(IDatasetItem item)
    {
        return SelectedItems.Contains(item);
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
