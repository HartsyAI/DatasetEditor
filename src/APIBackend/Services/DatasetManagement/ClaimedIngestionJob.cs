namespace DatasetStudio.APIBackend.Services.DatasetManagement;

/// <summary>A job a worker has claimed (or that was running when the server stopped).</summary>
/// <param name="Id">The job row's id.</param>
/// <param name="Attempts">How many times the job has been claimed, including this one.</param>
/// <param name="ItemsBefore">The dataset's item count when this attempt started, once recorded.</param>
/// <param name="Job">What to run. A HuggingFace token is already decrypted.</param>
/// <param name="PayloadError">
/// Set when the stored request could not be read back (for example, the Data Protection key that
/// encrypted its token is gone). The job cannot run and must be failed with this message.
/// </param>
public sealed record ClaimedIngestionJob(
    Guid Id,
    int Attempts,
    long? ItemsBefore,
    IngestionJob Job,
    string? PayloadError = null);
