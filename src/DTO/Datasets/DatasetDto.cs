namespace DatasetStudio.DTO.Datasets;

/// <summary>
/// One dataset as the API returns it (<c>GET /api/datasets/{id}</c>, create, status polling):
/// the library fields of <see cref="DatasetSummaryDto"/> plus what only the dataset's own page
/// needs. This was <c>DatasetDetailDto</c>; the JSON is the same apart from property order.
/// </summary>
public sealed record DatasetDto : DatasetSummaryDto
{
    /// <summary>The uploaded file's name, for an upload.</summary>
    public string? SourceFileName { get; init; }

    /// <summary>Why ingestion failed, when it did.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Whether the requesting user may manage this dataset's shares (owner, admin, or a share
    /// with the share right). Set by <c>GET /api/datasets/{id}</c> for the caller; false elsewhere.
    /// </summary>
    public bool CanShare { get; init; }
}
