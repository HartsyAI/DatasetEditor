using DatasetStudio.ClientApp.Features.Datasets.Services;
using DatasetStudio.DTO.Datasets;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace DatasetStudio.ClientApp.Features.Datasets.Components;

/// <summary>
/// One row of the list view. Keyboard reachable (Enter or Space opens it, like a click). The
/// Download and "more" buttons used to do nothing; Download now opens the original image and
/// "more" is gone. Image URLs go through <see cref="ImageUrlHelper"/>, so relative API paths
/// resolve against the API in the two-process dev setup (the row used the raw URL before).
/// </summary>
public partial class ImageListRow
{
    [Inject] private ImageUrlHelper ImageUrlHelper { get; set; } = default!;

    /// <summary>The item to render.</summary>
    [Parameter, EditorRequired] public DatasetItemDto Item { get; set; } = default!;

    /// <summary>Whether this row is currently selected.</summary>
    [Parameter] public bool IsSelected { get; set; }

    /// <summary>Raised when the row is opened (click, Enter or Space).</summary>
    [Parameter] public EventCallback<DatasetItemDto> OnClick { get; set; }

    /// <summary>Raised when the favourite toggle is pressed.</summary>
    [Parameter] public EventCallback<DatasetItemDto> OnToggleSelect { get; set; }

    private string? _thumbnailUrl;
    private string? _imageUrl;

    /// <summary>Non-empty facts shown as chips (dimensions, size, format).</summary>
    private IEnumerable<string> Facts =>
        new[] { Item.GetFormattedDimensions(), Item.GetFormattedFileSize(), Item.Format() }
            .Where(fact => !string.IsNullOrWhiteSpace(fact));

    /// <summary>Resolves the row's image URLs.</summary>
    protected override void OnParametersSet()
    {
        string? thumbnail = string.IsNullOrEmpty(Item.ThumbnailUrl) ? Item.ImageUrl : Item.ThumbnailUrl;
        _thumbnailUrl = string.IsNullOrEmpty(thumbnail)
            ? null
            : ImageUrlHelper.WithWidth(ImageUrlHelper.ResolveImageUrl(thumbnail), 240);
        _imageUrl = string.IsNullOrEmpty(Item.ImageUrl) ? null : ImageUrlHelper.ResolveImageUrl(Item.ImageUrl);
    }

    private Task HandleClick() => OnClick.InvokeAsync(Item);

    private Task HandleKeyDown(KeyboardEventArgs args)
    {
        return args.Key is "Enter" or " " ? HandleClick() : Task.CompletedTask;
    }

    private Task HandleToggleSelect() => OnToggleSelect.InvokeAsync(Item);
}
