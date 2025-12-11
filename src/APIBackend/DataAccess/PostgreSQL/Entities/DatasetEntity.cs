using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DatasetStudio.APIBackend.DataAccess.PostgreSQL.Entities;

/// <summary>
/// Database entity representing a dataset in PostgreSQL.
/// Maps to the 'datasets' table.
/// </summary>
[Table("datasets")]
public class DatasetEntity
{
    /// <summary>
    /// Primary key - unique identifier for the dataset
    /// </summary>
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    /// <summary>
    /// Display name of the dataset
    /// </summary>
    [Required]
    [MaxLength(200)]
    [Column("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional description of the dataset
    /// </summary>
    [Column("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Dataset format (e.g., "ImageFolder", "Parquet", "HuggingFace")
    /// </summary>
    [Required]
    [MaxLength(50)]
    [Column("format")]
    public string Format { get; set; } = string.Empty;

    /// <summary>
    /// Modality type (e.g., "Image", "Text", "Audio", "Video")
    /// </summary>
    [Required]
    [MaxLength(50)]
    [Column("modality")]
    public string Modality { get; set; } = string.Empty;

    /// <summary>
    /// Total number of items in the dataset
    /// </summary>
    [Column("item_count")]
    public int ItemCount { get; set; }

    /// <summary>
    /// Total size in bytes of the dataset
    /// </summary>
    [Column("total_size_bytes")]
    public long TotalSizeBytes { get; set; }

    /// <summary>
    /// Storage path where dataset files are located (relative or absolute)
    /// </summary>
    [MaxLength(500)]
    [Column("storage_path")]
    public string? StoragePath { get; set; }

    /// <summary>
    /// Path to the Parquet file storing dataset items (if applicable)
    /// </summary>
    [MaxLength(500)]
    [Column("parquet_path")]
    public string? ParquetPath { get; set; }

    /// <summary>
    /// Optional HuggingFace repository identifier
    /// </summary>
    [MaxLength(200)]
    [Column("huggingface_repo_id")]
    public string? HuggingFaceRepoId { get; set; }

    /// <summary>
    /// Optional HuggingFace dataset split (e.g., "train", "validation", "test")
    /// </summary>
    [MaxLength(50)]
    [Column("huggingface_split")]
    public string? HuggingFaceSplit { get; set; }

    /// <summary>
    /// Indicates if the dataset is public (multi-user support)
    /// </summary>
    [Column("is_public")]
    public bool IsPublic { get; set; }

    /// <summary>
    /// JSON metadata for additional dataset properties
    /// </summary>
    [Column("metadata", TypeName = "jsonb")]
    public string? Metadata { get; set; }

    /// <summary>
    /// Timestamp when the dataset was created
    /// </summary>
    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when the dataset was last updated
    /// </summary>
    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// User ID of the creator (null for single-user mode)
    /// </summary>
    [Column("created_by_user_id")]
    public Guid? CreatedByUserId { get; set; }

    // Navigation properties

    /// <summary>
    /// The user who created this dataset
    /// </summary>
    [ForeignKey(nameof(CreatedByUserId))]
    public UserEntity? CreatedByUser { get; set; }

    /// <summary>
    /// Captions associated with items in this dataset
    /// </summary>
    public ICollection<CaptionEntity> Captions { get; set; } = new List<CaptionEntity>();

    /// <summary>
    /// Permissions granted on this dataset
    /// </summary>
    public ICollection<PermissionEntity> Permissions { get; set; } = new List<PermissionEntity>();
}
