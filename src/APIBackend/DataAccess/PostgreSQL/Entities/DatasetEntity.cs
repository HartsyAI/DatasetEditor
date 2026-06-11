using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DatasetStudio.DTO.Datasets;

namespace DatasetStudio.APIBackend.DataAccess.PostgreSQL.Entities;

/// <summary>
/// Database entity representing a dataset in PostgreSQL.
/// Maps to the 'datasets' table.
/// </summary>
[Table("datasets")]
public class DatasetEntity
{
    /// <summary>Primary key - unique identifier for the dataset</summary>
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    /// <summary>Display name of the dataset</summary>
    [Required]
    [MaxLength(200)]
    [Column("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional description of the dataset</summary>
    [Column("description")]
    public string? Description { get; set; }

    /// <summary>Current ingestion/processing status</summary>
    [Required]
    [Column("status")]
    public IngestionStatusDto Status { get; set; } = IngestionStatusDto.Pending;

    /// <summary>Dataset format (e.g., "CSV", "Parquet", "HuggingFace")</summary>
    [Required]
    [MaxLength(50)]
    [Column("format")]
    public string Format { get; set; } = "Unknown";

    /// <summary>Modality type (e.g., "Image", "Text", "Audio", "Video")</summary>
    [Required]
    [MaxLength(50)]
    [Column("modality")]
    public string Modality { get; set; } = "Image";

    /// <summary>Total number of items in the dataset</summary>
    [Column("total_items")]
    public long TotalItems { get; set; }

    /// <summary>Total size in bytes of the dataset</summary>
    [Column("total_size_bytes")]
    public long TotalSizeBytes { get; set; }

    /// <summary>Original uploaded file name (if from upload)</summary>
    [MaxLength(500)]
    [Column("source_file_name")]
    public string? SourceFileName { get; set; }

    /// <summary>Dataset source type</summary>
    [Required]
    [Column("source_type")]
    public DatasetSourceType SourceType { get; set; } = DatasetSourceType.LocalUpload;

    /// <summary>Source URI (for HuggingFace, web datasets, etc.)</summary>
    [MaxLength(1000)]
    [Column("source_uri")]
    public string? SourceUri { get; set; }

    /// <summary>Whether this dataset is streaming (HuggingFace streaming mode)</summary>
    [Column("is_streaming")]
    public bool IsStreaming { get; set; }

    /// <summary>HuggingFace repository identifier (e.g., "nlphuji/flickr30k")</summary>
    [MaxLength(200)]
    [Column("huggingface_repository")]
    public string? HuggingFaceRepository { get; set; }

    /// <summary>HuggingFace dataset config/subset</summary>
    [MaxLength(100)]
    [Column("huggingface_config")]
    public string? HuggingFaceConfig { get; set; }

    /// <summary>HuggingFace dataset split (e.g., "train", "validation", "test")</summary>
    [MaxLength(50)]
    [Column("huggingface_split")]
    public string? HuggingFaceSplit { get; set; }

    /// <summary>Storage path where dataset files are located on disk</summary>
    [MaxLength(1000)]
    [Column("storage_path")]
    public string? StoragePath { get; set; }

    /// <summary>Path to the Parquet file storing dataset items (for non-streaming datasets)</summary>
    [MaxLength(1000)]
    [Column("parquet_path")]
    public string? ParquetPath { get; set; }

    /// <summary>Error message if ingestion/processing failed</summary>
    [Column("error_message")]
    public string? ErrorMessage { get; set; }

    /// <summary>Indicates if the dataset is public (for future multi-user support)</summary>
    [Column("is_public")]
    public bool IsPublic { get; set; } = true;

    /// <summary>JSON metadata for additional dataset properties</summary>
    [Column("metadata", TypeName = "jsonb")]
    public string? Metadata { get; set; }

    /// <summary>Timestamp when the dataset was created</summary>
    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Timestamp when the dataset was last updated</summary>
    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>User ID of the creator (null for single-user mode, set in Phase 3)</summary>
    [Column("created_by_user_id")]
    public Guid? CreatedByUserId { get; set; }

    // Navigation properties (for Phase 3 - Multi-user support)

    /// <summary>The user who created this dataset</summary>
    [ForeignKey(nameof(CreatedByUserId))]
    public UserEntity? CreatedByUser { get; set; }

    /// <summary>Captions associated with items in this dataset</summary>
    public ICollection<CaptionEntity> Captions { get; set; } = new List<CaptionEntity>();

    /// <summary>Permissions granted on this dataset</summary>
    public ICollection<PermissionEntity> Permissions { get; set; } = new List<PermissionEntity>();
}
