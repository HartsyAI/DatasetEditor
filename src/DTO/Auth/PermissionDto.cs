namespace DatasetStudio.DTO.Auth;

/// <summary>
/// A grant of access to one dataset for one user, mirroring PermissionEntity. AccessLevel is
/// free text ("Read"/"Write"/"Admin"/"Owner") rather than an enum, matching the existing schema.
/// </summary>
public sealed record PermissionDto
{
    public Guid Id { get; init; }
    public Guid DatasetId { get; init; }
    public Guid UserId { get; init; }
    public string AccessLevel { get; init; } = "Read";
    public bool CanShare { get; init; }
    public bool CanDelete { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public DateTime GrantedAt { get; init; }
    public Guid? GrantedByUserId { get; init; }

    public bool IsExpired(DateTime asOfUtc) => ExpiresAt.HasValue && ExpiresAt.Value <= asOfUtc;
}
