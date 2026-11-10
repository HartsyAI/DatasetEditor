using System.ComponentModel.DataAnnotations.Schema;

namespace DatasetStudio.APIBackend.DataAccess.PostgreSQL.Entities;

/// <summary>
/// Edits of one streamed item (table <c>item_overlays</c>). HuggingFace rows and S3 objects are
/// read from their source on every request and can't be written back, so their edits are kept
/// here and applied over each page as it is served (audit E-3). A null field is not overridden:
/// the source's value shows. The key is the dataset and the item's derived id
/// (<see cref="Services.DatasetManagement.StreamingItemIds"/>).
/// </summary>
[Table("item_overlays")]
public class ItemOverlayEntity
{
    /// <summary>The item's dataset (cascade: deleting the dataset deletes its overlays).</summary>
    [Column("dataset_id")]
    public Guid DatasetId { get; set; }

    /// <summary>The item's derived id.</summary>
    [Column("item_id")]
    public Guid ItemId { get; set; }

    /// <summary>The edited title, or null when not edited.</summary>
    [Column("title")]
    public string? Title { get; set; }

    /// <summary>The edited description, or null when not edited.</summary>
    [Column("description")]
    public string? Description { get; set; }

    /// <summary>The item's whole tag list as a JSON array once tags were edited, else null.</summary>
    [Column("tags", TypeName = "jsonb")]
    public string? TagsJson { get; set; }

    /// <summary>The favourite flag, or null when never set.</summary>
    [Column("is_favorite")]
    public bool? IsFavorite { get; set; }

    /// <summary>Edited metadata keys as a JSON object, merged over the source's metadata; null when none.</summary>
    [Column("metadata", TypeName = "jsonb")]
    public string? MetadataJson { get; set; }

    /// <summary>
    /// Edit counter, the item's version for <c>If-Match</c> (audit E-4). 0 is a row created by an
    /// edit that has not committed a change yet, which reads the same as no row.
    /// </summary>
    [Column("version")]
    public int Version { get; set; }

    /// <summary>When the overlay last changed.</summary>
    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }

    /// <summary>The dataset.</summary>
    public DatasetEntity? Dataset { get; set; }
}
