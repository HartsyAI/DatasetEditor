using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using HartsysDatasetEditor.Client.Services;
using HartsysDatasetEditor.Client.Services.StateManagement;
using HartsysDatasetEditor.Core.Models;
using HartsysDatasetEditor.Core.Utilities;

namespace HartsysDatasetEditor.Client.Components.Viewer;

/// <summary>Enhanced image card component with 3-tier metadata display</summary>
public partial class ImageCard
{
    [Inject] public ViewState ViewState { get; set; } = default!;
    [Inject] public DatasetState DatasetState { get; set; } = default!;
    [Inject] public ItemEditService EditService { get; set; } = default!;
    [Inject] public ImageUrlHelper ImageUrlHelper { get; set; } = default!;

    /// <summary>The image item to display.</summary>
    [Parameter] public ImageItem Item { get; set; } = default!;

    /// <summary>Indicates whether this item is currently selected.</summary>
    [Parameter] public bool IsSelected { get; set; }

    /// <summary>Event callback when the card is clicked.</summary>
    [Parameter] public EventCallback<ImageItem> OnClick { get; set; }

    /// <summary>Event callback when the selection checkbox is toggled.</summary>
    [Parameter] public EventCallback<ImageItem> OnToggleSelect { get; set; }
    
    /// <summary>Event callback when edit is clicked.</summary>
    [Parameter] public EventCallback<ImageItem> OnEdit { get; set; }

    private bool _isHovered = false;
    private bool _imageLoaded = false;
    private bool _imageError = false;
    private string _imageUrl = string.Empty;
    private bool _isEditingTitle = false;
    private string _editTitle = string.Empty;

    /// <summary>Initializes component and prepares image URL.</summary>
    protected override void OnInitialized()
    {
        PrepareImageUrl();
    }

    /// <summary>Updates component when parameters change.</summary>
    protected override void OnParametersSet()
    {
        PrepareImageUrl();
    }

    /// <summary>Prepares the image URL with optional transformations.</summary>
    public void PrepareImageUrl()
    {
        if (string.IsNullOrEmpty(Item.ImageUrl))
        {
            _imageUrl = string.Empty;
            _imageError = true;
            _imageLoaded = false;
            return;
        }

        // Use thumbnail URL if available, otherwise use regular image URL
        string baseUrl = string.IsNullOrEmpty(Item.ThumbnailUrl)
            ? Item.ImageUrl
            : Item.ThumbnailUrl;

        // Resolve to full URL (prepends API base address if relative)
        _imageUrl = ImageUrlHelper.ResolveImageUrl(baseUrl);
        _imageLoaded = true;
        _imageError = false;

        // TODO: Add image transformation parameters (resize, quality) using ImageHelper
        // Example: _imageUrl = ImageHelper.AddResizeParams(_imageUrl, width: 400, height: 400);
    }

    /// <summary>Handles mouse enter event.</summary>
    public void HandleMouseEnter()
    {
        _isHovered = true;
    }

    /// <summary>Handles mouse leave event.</summary>
    public void HandleMouseLeave()
    {
        _isHovered = false;
    }

    /// <summary>Handles click event on the card.</summary>
    public async Task HandleClick()
    {
        await OnClick.InvokeAsync(Item);
    }

    /// <summary>Handles selection checkbox toggle.</summary>
    public async Task HandleToggleSelect()
    {
        await OnToggleSelect.InvokeAsync(Item);
    }

    /// <summary>Toggles favorite status.</summary>
    public void HandleToggleFavorite()
    {
        Item.IsFavorite = !Item.IsFavorite;
        DatasetState.UpdateItem(Item);
        StateHasChanged();
    }

    /// <summary>Handles image load error.</summary>
    public void HandleImageError()
    {
        _imageError = true;
        _imageLoaded = false;
        Logs.Error($"Failed to load image for item: {Item.Id}");
    }
    
    /// <summary>Starts inline title edit.</summary>
    public void StartEditTitle()
    {
        _isEditingTitle = true;
        _editTitle = Item.Title ?? string.Empty;
    }

    /// <summary>Saves the edited title via ItemEditService.</summary>
    public async Task SaveTitle()
    {
        if (Item == null)
        {
            _isEditingTitle = false;
            return;
        }

        bool wasEditing = _isEditingTitle;
        _isEditingTitle = false;

        if (!wasEditing || _editTitle == Item.Title)
        {
            return;
        }

        bool success = await EditService.UpdateItemAsync(Item, title: _editTitle);
        if (!success)
        {
            // Revert on failure
            _editTitle = Item.Title ?? string.Empty;
        }
    }

    /// <summary>Handles key events while editing the title.</summary>
    public async Task HandleTitleKeyUp(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            await SaveTitle();
        }
        else if (e.Key == "Escape")
        {
            _isEditingTitle = false;
            _editTitle = Item.Title ?? string.Empty;
        }
    }
    
    /// <summary>Handles download button click.</summary>
    public void HandleDownload()
    {
        // TODO: Implement download functionality
        Logs.Info($"Download requested for: {Item.Id}");
    }

    /// <summary>Handles edit button click.</summary>
    public async Task HandleEditClick()
    {
        await OnEdit.InvokeAsync(Item);
    }

    /// <summary>Handles menu button click.</summary>
    public void HandleMenuClick()
    {
        // TODO: Show context menu
        Logs.Info($"Menu clicked for: {Item.Id}");
    }

    /// <summary>Gets display title with truncation.</summary>
    public string GetDisplayTitle()
    {
        if (string.IsNullOrEmpty(Item.Title))
            return "Untitled";
        
        return Item.Title.Length > 30 
            ? Item.Title.Substring(0, 27) + "..." 
            : Item.Title;
    }

    /// <summary>Gets truncated description for hover overlay.</summary>
    public string GetTruncatedDescription()
    {
        if (string.IsNullOrEmpty(Item.Description))
            return string.Empty;
        
        return Item.Description.Length > 100 
            ? Item.Description.Substring(0, 97) + "..." 
            : Item.Description;
    }
    
    // TODO: Add context menu on right-click (download, favorite, delete, etc.)
    // TODO: Add quick actions toolbar on hover (favorite icon, download icon)
    // TODO: Add LQIP (Low Quality Image Placeholder) blur technique
    // TODO: Add IntersectionObserver for more advanced lazy loading control
    // TODO: Add image zoom on hover option
    // TODO: Add keyboard focus support for accessibility
}
