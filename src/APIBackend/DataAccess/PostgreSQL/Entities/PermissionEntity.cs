using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DatasetStudio.APIBackend.DataAccess.PostgreSQL.Entities;

/// <summary>
/// Database entity representing user permissions for datasets.
/// Maps to the 'permissions' table.
/// </summary>
[Table("permissions")]
public class PermissionEntity
{
    /// <summary>
    /// Primary key - unique identifier for the permission
    /// </summary>
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    /// <summary>
    /// Foreign key to the dataset
    /// </summary>
    [Required]
    [Column("dataset_id")]
    public Guid DatasetId { get; set; }

    /// <summary>
    /// Foreign key to the user
    /// </summary>
    [Required]
    [Column("user_id")]
    public Guid UserId { get; set; }

    /// <summary>
    /// Access level granted (e.g., "Read", "Write", "Admin", "Owner")
    /// </summary>
    [Required]
    [MaxLength(50)]
    [Column("access_level")]
    public string AccessLevel { get; set; } = string.Empty;

    /// <summary>
    /// Indicates if the user can share this dataset with others
    /// </summary>
    [Column("can_share")]
    public bool CanShare { get; set; } = false;

    /// <summary>
    /// Indicates if the user can delete this dataset
    /// </summary>
    [Column("can_delete")]
    public bool CanDelete { get; set; } = false;

    /// <summary>
    /// Optional expiration date for the permission
    /// </summary>
    [Column("expires_at")]
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Timestamp when the permission was granted
    /// </summary>
    [Column("granted_at")]
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// User ID of who granted this permission
    /// </summary>
    [Column("granted_by_user_id")]
    public Guid? GrantedByUserId { get; set; }

    /// <summary>
    /// Timestamp when the permission was last updated
    /// </summary>
    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties

    /// <summary>
    /// The dataset this permission applies to
    /// </summary>
    [ForeignKey(nameof(DatasetId))]
    public DatasetEntity Dataset { get; set; } = null!;

    /// <summary>
    /// The user who has this permission
    /// </summary>
    [ForeignKey(nameof(UserId))]
    public UserEntity User { get; set; } = null!;

    /// <summary>
    /// The user who granted this permission
    /// </summary>
    [ForeignKey(nameof(GrantedByUserId))]
    public UserEntity? GrantedByUser { get; set; }
}
