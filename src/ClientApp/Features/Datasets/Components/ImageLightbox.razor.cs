using DatasetStudio.ClientApp.Features.Datasets.Services;
using DatasetStudio.Core.Utilities.Logging;
using DatasetStudio.DTO.Datasets;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace DatasetStudio.ClientApp.Features.Datasets.Components;

/// <summary>
/// Full-size image preview with the item's metadata, shown in a dialog (Escape closes it and the
/// dialog traps focus). Opened from the detail panel.
/// </summary>
public partial class ImageLightbox
{
    [CascadingParameter] public MudDialogInstance Dialog { get; set; } = default!;
    [Inject] public ImageUrlHelper ImageUrlHelper { get; set; } = default!;
    
    [Parameter] public string? ImageUrl { get; set; }
    [Parameter] public DatasetItemDto? Item { get; set; }
    
    private string _imageUrl => ImageUrlHelper.ResolveImageUrl(ImageUrl ?? Item?.ImageUrl);
    
    private string DisplayTitle => string.IsNullOrWhiteSpace(Item?.Title)
        ? (Item?.Id.ToString() ?? "Image")
        : Item!.Title;
    
    private string PrimaryInfoLine => Item == null
        ? string.Empty
        : string.Join(" • ", new[]
        {
            GetPhotographerLabel(),
            GetLocationLabel(),
            Item.CreatedAt != default ? Item.CreatedAt.ToString("MMM dd, yyyy") : null
        }.Where(s => !string.IsNullOrWhiteSpace(s)));
    
    private string? AverageColorHex => GetMetadataValue("color_hex")
        ?? GetMetadataValue("average_color")
        ?? (Item != null ? Item.AverageColor() : null);
    
    private readonly List<(string Key, string Value)> _highlightedMetadata = new();
    private readonly List<(string Key, string Value)> _additionalMetadata = new();
    private readonly List<string> _tagList = new();
    
    protected override void OnParametersSet()
    {
        BuildMetadataCollections();
    }
    
    private void BuildMetadataCollections()
    {
        _highlightedMetadata.Clear();
        _additionalMetadata.Clear();
        _tagList.Clear();
    
        if (Item?.Metadata is null)
        {
            return;
        }
    
        string[] highlightedKeys =
        {
            "photographer_username",
            "photographer_name",
            "photo_url",
            "photo_location_name",
            "photo_location_latitude",
            "photo_location_longitude",
            "color_hex",
            "dominant_color",
            "likes",
            "downloads",
            "views"
        };
    
        foreach (string key in highlightedKeys)
        {
            string? value = GetMetadataValue(key);
            if (!string.IsNullOrWhiteSpace(value))
            {
                _highlightedMetadata.Add((FormatKey(key), value));
            }
        }
    
        foreach ((string key, string value) in Item.Metadata)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }
    
            bool alreadyAdded = _highlightedMetadata.Any(k => string.Equals(k.Key, FormatKey(key), StringComparison.OrdinalIgnoreCase));
            if (!alreadyAdded)
            {
                _additionalMetadata.Add((FormatKey(key), value));
            }
        }
    
        BuildTagList();
    }
    
    private void BuildTagList()
    {
        HashSet<string> tags = new(StringComparer.OrdinalIgnoreCase);
    
        if (Item?.Tags != null)
        {
            foreach (string tag in Item.Tags.Where(tag => !string.IsNullOrWhiteSpace(tag)))
            {
                tags.Add(tag.Trim());
            }
        }
    
        string[] metadataTagKeys = { "keywords", "tags", "labels", "topics", "categories" };
        foreach (string key in metadataTagKeys)
        {
            string? raw = GetMetadataValue(key);
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }
    
            foreach (string tag in raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                tags.Add(tag.Trim());
            }
        }
    
        _tagList.AddRange(tags.OrderBy(t => t));
    }
    
    private string? GetMetadataValue(string key)
    {
        if (Item?.Metadata != null && Item.Metadata.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }
    
        return null;
    }
    
    private static string FormatKey(string key) => key.Replace('_', ' ');
    
    private string? GetPhotographerLabel()
    {
        string? photographer = Item?.Photographer();
        photographer ??= GetMetadataValue("photographer_name") ?? GetMetadataValue("photographer_username");
        return photographer is null ? null : $"By {photographer}";
    }
    
    private string? GetLocationLabel()
    {
        string? location = (Item != null ? Item.Location() : null) ?? GetMetadataValue("photo_location_name") ?? GetMetadataValue("location");
        if (string.IsNullOrWhiteSpace(location))
        {
            return null;
        }
    
        string? lat = GetMetadataValue("photo_location_latitude");
        string? lon = GetMetadataValue("photo_location_longitude");
        return !string.IsNullOrWhiteSpace(lat) && !string.IsNullOrWhiteSpace(lon)
            ? $"{location} ({lat}, {lon})"
            : location;
    }
    
    private Task CloseAsync()
    {
        Dialog.Close(DialogResult.Cancel());
        return Task.CompletedTask;
    }
    
}
