using DatasetStudio.APIBackend.Services.DatasetManagement;
using DatasetStudio.DTO.Datasets;

namespace DatasetStudio.APIBackend.DataAccess.PostgreSQL.Repositories;

/// <summary>
/// The edits stored for one streamed item (see <see cref="Entities.ItemOverlayEntity"/>). A null
/// field is not overridden. <see cref="Version"/> is 0 for an item that was never edited.
/// </summary>
public sealed record ItemOverlay
{
    /// <summary>The overlay of an item that was never edited.</summary>
    public static ItemOverlay Empty { get; } = new();

    /// <summary>Replaces the title when set.</summary>
    public string? Title { get; init; }

    /// <summary>Replaces the description when set.</summary>
    public string? Description { get; init; }

    /// <summary>Replaces the whole tag list when set.</summary>
    public List<string>? Tags { get; init; }

    /// <summary>Replaces the favourite flag when set.</summary>
    public bool? IsFavorite { get; init; }

    /// <summary>Keys merged over the source's metadata when set.</summary>
    public Dictionary<string, string>? Metadata { get; init; }

    /// <summary>The edit counter: the item's version.</summary>
    public int Version { get; init; }

    /// <summary>
    /// The item as served: overridden fields replace the source's, edited metadata keys are merged
    /// over the source's metadata, and the version is this overlay's.
    /// </summary>
    public DatasetItemDto ApplyTo(DatasetItemDto item)
    {
        Dictionary<string, string> metadata = item.Metadata;
        if (Metadata is not null)
        {
            metadata = new Dictionary<string, string>(item.Metadata, StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> entry in Metadata)
            {
                metadata[entry.Key] = entry.Value;
            }
        }

        return item with
        {
            Title = Title ?? item.Title,
            Description = Description ?? item.Description,
            Tags = Tags is null ? item.Tags : new List<string>(Tags),
            IsFavorite = IsFavorite ?? item.IsFavorite,
            Metadata = metadata,
            Version = ItemVersions.ForOverlay(Version)
        };
    }
}
