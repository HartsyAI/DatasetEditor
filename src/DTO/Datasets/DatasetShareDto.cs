namespace DatasetStudio.DTO.Datasets;

/// <summary>One user's access to a dataset, as listed by <c>GET /api/datasets/{id}/shares</c>.</summary>
public sealed record DatasetShareDto
{
    /// <summary>The user the dataset is shared with.</summary>
    public Guid UserId { get; init; }

    /// <summary>Their username.</summary>
    public string Username { get; init; } = string.Empty;

    /// <summary>Their display name, when set.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Read or write.</summary>
    public DatasetAccessLevel AccessLevel { get; init; }

    /// <summary>Whether they may delete the dataset.</summary>
    public bool CanDelete { get; init; }

    /// <summary>Whether they may share it with others (never beyond their own access).</summary>
    public bool CanShare { get; init; }

    /// <summary>When the share ends (UTC), or null for no end.</summary>
    public DateTime? ExpiresAt { get; init; }

    /// <summary>When it was granted or last changed (UTC).</summary>
    public DateTime GrantedAt { get; init; }

    /// <summary>Who granted it, when known.</summary>
    public string? GrantedByUsername { get; init; }
}
