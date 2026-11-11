namespace DatasetStudio.APIBackend.DataAccess.PostgreSQL.Repositories;

/// <summary>
/// Row-visibility scope for GetAllAsync/GetCountAsync, applied at the SQL level via
/// the repository's own query -- not by filtering an already-fetched, already-paged list, which
/// would silently under-fill a page of results (the same bug class Phase 1 fixed for item-cursor
/// filtering). Pass null for "no filter" -- internal/background callers that need every row
/// regardless of ownership (e.g. the ingestion pipeline) should keep doing that.
/// </summary>
public readonly record struct DatasetVisibilityFilter(Guid? RequestingUserId, bool IsAdmin)
{
    public static DatasetVisibilityFilter ForAnonymous() => new(null, false);
    public static DatasetVisibilityFilter ForUser(Guid userId, bool isAdmin) => new(userId, isAdmin);
}
