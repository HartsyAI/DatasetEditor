using Microsoft.AspNetCore.Components;
using HartsysDatasetEditor.Client.Services.StateManagement;
using HartsysDatasetEditor.Core.Models;
using HartsysDatasetEditor.Core.Utilities;

namespace HartsysDatasetEditor.Client.Components.Viewer;

/// <summary>Individual image card component with lazy loading, selection, and metadata overlay.</summary>
public partial class ImageCard
{
    [Inject] public ViewState ViewState { get; set; } = default!;

    /// <summary>The image item to display.</summary>
    [Parameter] public ImageItem Item { get; set; } = default!;

    /// <summary>Indicates whether this item is currently selected.</summary>
    [Parameter] public bool IsSelected { get; set; }

    /// <summary>Event callback when the card is clicked.</summary>
    [Parameter] public EventCallback<ImageItem> OnClick { get; set; }

    /// <summary>Event callback when the selection checkbox is toggled.</summary>
    [Parameter] public EventCallback<ImageItem> OnToggleSelect { get; set; }

    public bool _isHovered = false;
    public bool _imageLoaded = false;
    public bool _imageError = false;
    public bool _showMetadataOverlay = true;
    public string _imageUrl = string.Empty;

    /// <summary>Initializes component and prepares image URL.</summary>
    protected override void OnInitialized()
    {
        _showMetadataOverlay = ViewState.Settings.ShowMetadataOverlay;
        PrepareImageUrl();
        Logs.Info($"ImageCard initialized for item: {Item.Id}");
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
        _imageUrl = string.IsNullOrEmpty(Item.ThumbnailUrl) 
            ? Item.ImageUrl 
            : Item.ThumbnailUrl;

        _imageLoaded = true;
        _imageError = false;

        // TODO: Add image transformation parameters (resize, quality) using ImageHelper
        // Example: _imageUrl = ImageHelper.AddResizeParams(_imageUrl, width: 400, height: 400);
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

    /// <summary>Handles image load error.</summary>
    public void HandleImageError()
    {
        _imageError = true;
        _imageLoaded = false;
        Logs.Error($"Failed to load image for item: {Item.Id}");
    }
    
    // TODO: Add context menu on right-click (download, favorite, delete, etc.)
    // TODO: Add quick actions toolbar on hover (favorite icon, download icon)
    // TODO: Add LQIP (Low Quality Image Placeholder) blur technique
    // TODO: Add IntersectionObserver for more advanced lazy loading control
    // TODO: Add image zoom on hover option
    // TODO: Add keyboard focus support for accessibility
}
