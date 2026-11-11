namespace DatasetStudio.APIBackend.DataAccess.PostgreSQL.Repositories;

/// <summary>
/// A dataset update was refused because a field it changes was changed by someone else after
/// the caller read the dataset. Endpoints answer 409 so the client can reload and retry.
/// </summary>
public sealed class DatasetUpdateConflictException : Exception
{
    /// <summary>Creates the exception for one dataset and the fields that conflicted.</summary>
    /// <param name="datasetId">The dataset being updated.</param>
    /// <param name="fields">Names of the fields changed concurrently.</param>
    public DatasetUpdateConflictException(Guid datasetId, IReadOnlyCollection<string> fields)
        : base($"Dataset {datasetId} was changed by another request ({string.Join(", ", fields)}). Reload it and try again.")
    {
        DatasetId = datasetId;
        Fields = fields;
    }

    /// <summary>The dataset being updated.</summary>
    public Guid DatasetId { get; }

    /// <summary>Names of the fields changed concurrently.</summary>
    public IReadOnlyCollection<string> Fields { get; }
}
