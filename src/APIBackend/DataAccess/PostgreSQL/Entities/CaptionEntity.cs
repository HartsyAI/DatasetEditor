using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DatasetStudio.APIBackend.DataAccess.PostgreSQL.Entities;

/// <summary>
/// Database entity representing a caption/annotation for a dataset item.
/// Maps to the 'captions' table.
/// </summary>
[Table("captions")]
public class CaptionEntity
{
    /// <summary>
    /// Primary key - unique identifier for the caption
    /// </summary>
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    /// <summary>
    /// Foreign key to the dataset this caption belongs to
    /// </summary>
    [Required]
    [Column("dataset_id")]
    public Guid DatasetId { get; set; }

    /// <summary>
    /// Identifier of the specific item within the dataset (e.g., file name, index)
    /// </summary>
    [Required]
    [MaxLength(500)]
    [Column("item_id")]
    public string ItemId { get; set; } = string.Empty;

    /// <summary>
    /// The caption text
    /// </summary>
    [Required]
    [Column("text")]
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Source of the caption (e.g., "Manual", "BLIP", "GPT-4", "Original")
    /// </summary>
    [Required]
    [MaxLength(100)]
    [Column("source")]
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Optional quality/confidence score (0.0 to 1.0)
    /// </summary>
    [Column("score")]
    public float? Score { get; set; }

    /// <summary>
    /// Language code (e.g., "en", "es", "fr")
    /// </summary>
    [MaxLength(10)]
    [Column("language")]
    public string? Language { get; set; }

    /// <summary>
    /// Indicates if this is the primary/active caption for the item
    /// </summary>
    [Column("is_primary")]
    public bool IsPrimary { get; set; } = false;

    /// <summary>
    /// JSON metadata for additional caption properties
    /// </summary>
    [Column("metadata", TypeName = "jsonb")]
    public string? Metadata { get; set; }

    /// <summary>
    /// Timestamp when the caption was created
    /// </summary>
    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// User ID of the creator (null for AI-generated)
    /// </summary>
    [Column("created_by_user_id")]
    public Guid? CreatedByUserId { get; set; }

    /// <summary>
    /// Timestamp when the caption was last updated
    /// </summary>
    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties

    /// <summary>
    /// The dataset this caption belongs to
    /// </summary>
    [ForeignKey(nameof(DatasetId))]
    public DatasetEntity Dataset { get; set; } = null!;

    /// <summary>
    /// The user who created this caption (if applicable)
    /// </summary>
    [ForeignKey(nameof(CreatedByUserId))]
    public UserEntity? CreatedByUser { get; set; }
}
