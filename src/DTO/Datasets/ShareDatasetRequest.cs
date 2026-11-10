namespace DatasetStudio.DTO.Datasets;

/// <summary>
/// Body of <c>PUT /api/datasets/{id}/shares</c>: shares the dataset with an account, or changes
/// an existing share with it.
/// </summary>
/// <param name="UsernameOrEmail">The account to share with.</param>
/// <param name="AccessLevel">Read or write.</param>
/// <param name="CanDelete">Whether they may delete the dataset.</param>
/// <param name="CanShare">Whether they may share it onward.</param>
/// <param name="ExpiresAt">When the share ends (UTC), or null for no end.</param>
public sealed record ShareDatasetRequest(
    string UsernameOrEmail,
    DatasetAccessLevel AccessLevel = DatasetAccessLevel.Read,
    bool CanDelete = false,
    bool CanShare = false,
    DateTime? ExpiresAt = null)
{
    /// <summary>
    /// The account to share with, by id, when changing an existing share. Takes precedence over
    /// <see cref="UsernameOrEmail"/>: a username can equal another account's email, so resolving
    /// the name again could reach the wrong account.
    /// </summary>
    public Guid? UserId { get; init; }
}
