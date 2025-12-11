using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DatasetStudio.APIBackend.DataAccess.PostgreSQL.Entities;

/// <summary>
/// Database entity representing a user in PostgreSQL.
/// Maps to the 'users' table.
/// </summary>
[Table("users")]
public class UserEntity
{
    /// <summary>
    /// Primary key - unique identifier for the user
    /// </summary>
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    /// <summary>
    /// Unique username for login
    /// </summary>
    [Required]
    [MaxLength(100)]
    [Column("username")]
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// User's email address
    /// </summary>
    [Required]
    [MaxLength(200)]
    [Column("email")]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Hashed password (using bcrypt or similar)
    /// </summary>
    [Required]
    [MaxLength(500)]
    [Column("password_hash")]
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// User's display name
    /// </summary>
    [MaxLength(200)]
    [Column("display_name")]
    public string? DisplayName { get; set; }

    /// <summary>
    /// User role (e.g., "Admin", "User", "Guest")
    /// </summary>
    [Required]
    [MaxLength(50)]
    [Column("role")]
    public string Role { get; set; } = "User";

    /// <summary>
    /// Indicates if the user account is active
    /// </summary>
    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Indicates if the email has been verified
    /// </summary>
    [Column("email_verified")]
    public bool EmailVerified { get; set; } = false;

    /// <summary>
    /// Optional avatar/profile picture URL
    /// </summary>
    [MaxLength(500)]
    [Column("avatar_url")]
    public string? AvatarUrl { get; set; }

    /// <summary>
    /// JSON preferences for user settings
    /// </summary>
    [Column("preferences", TypeName = "jsonb")]
    public string? Preferences { get; set; }

    /// <summary>
    /// Timestamp when the user was created
    /// </summary>
    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp of last login
    /// </summary>
    [Column("last_login_at")]
    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// Timestamp when the user was last updated
    /// </summary>
    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties

    /// <summary>
    /// Datasets created by this user
    /// </summary>
    public ICollection<DatasetEntity> CreatedDatasets { get; set; } = new List<DatasetEntity>();

    /// <summary>
    /// Permissions granted to this user
    /// </summary>
    public ICollection<PermissionEntity> Permissions { get; set; } = new List<PermissionEntity>();
}
