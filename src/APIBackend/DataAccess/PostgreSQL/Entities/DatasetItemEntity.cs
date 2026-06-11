using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DatasetStudio.APIBackend.DataAccess.PostgreSQL.Entities;

/// <summary>
/// Database entity representing a single item/sample in a dataset.
/// Maps to the 'dataset_items' table.
/// NOTE: Large datasets should use Parquet storage instead of PostgreSQL for items.
/// This table is for metadata and small datasets only.
/// </summary>
[Table("dataset_items")]
public class DatasetItemEntity
{
    /// <summary>
    /// Primary key - unique identifier for the item
    /// </summary>
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    /// <summary>
    /// Foreign key to the dataset this item belongs to
    /// </summary>
    [Required]
    [Column("dataset_id")]
    public Guid DatasetId { get; set; }

    /// <summary>
    /// Unique identifier within the dataset (e.g., filename, row index)
    /// </summary>
    [Required]
    [MaxLength(500)]
    [Column("item_id")]
    public string ItemId { get; set; } = string.Empty;

    /// <summary>
    /// File path or URL to the item (for images, audio, video, etc.)
    /// </summary>
    [MaxLength(1000)]
    [Column("file_path")]
    public string? FilePath { get; set; }

    /// <summary>
    /// MIME type (e.g., "image/jpeg", "audio/wav")
    /// </summary>
    [MaxLength(100)]
    [Column("mime_type")]
    public string? MimeType { get; set; }

    /// <summary>
    /// File size in bytes
    /// </summary>
    [Column("file_size_bytes")]
    public long? FileSizeBytes { get; set; }

    /// <summary>
    /// Width (for images/videos)
    /// </summary>
    [Column("width")]
    public int? Width { get; set; }

    /// <summary>
    /// Height (for images/videos)
    /// </summary>
    [Column("height")]
    public int? Height { get; set; }

    /// <summary>
    /// Duration in seconds (for audio/video)
    /// </summary>
    [Column("duration_seconds")]
    public float? DurationSeconds { get; set; }

    /// <summary>
    /// Primary caption/label for the item
    /// </summary>
    [Column("caption")]
    public string? Caption { get; set; }

    /// <summary>
    /// Tags associated with the item (comma-separated or JSON array)
    /// </summary>
    [Column("tags")]
    public string? Tags { get; set; }

    /// <summary>
    /// Quality score (0.0 to 1.0)
    /// </summary>
    [Column("quality_score")]
    public float? QualityScore { get; set; }

    /// <summary>
    /// JSON metadata for additional item properties
    /// </summary>
    [Column("metadata", TypeName = "jsonb")]
    public string? Metadata { get; set; }

    /// <summary>
    /// Embedding vector for similarity search (stored as binary or JSON)
    /// </summary>
    [Column("embedding")]
    public byte[]? Embedding { get; set; }

    /// <summary>
    /// Indicates if the item is flagged for review
    /// </summary>
    [Column("is_flagged")]
    public bool IsFlagged { get; set; } = false;

    /// <summary>
    /// Indicates if the item has been deleted (soft delete)
    /// </summary>
    [Column("is_deleted")]
    public bool IsDeleted { get; set; } = false;

    /// <summary>
    /// Timestamp when the item was created
    /// </summary>
    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when the item was last updated
    /// </summary>
    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties

    /// <summary>
    /// The dataset this item belongs to
    /// </summary>
    [ForeignKey(nameof(DatasetId))]
    public DatasetEntity Dataset { get; set; } = null!;
}
