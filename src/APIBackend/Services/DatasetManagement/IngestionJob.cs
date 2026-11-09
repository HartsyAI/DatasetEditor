using DatasetStudio.DTO.Datasets;

namespace DatasetStudio.APIBackend.Services.DatasetManagement;

/// <summary>
/// Work for the ingestion worker: a staged upload to parse (<see cref="FilePath"/>), a
/// HuggingFace import (<see cref="HuggingFaceRequest"/>), or a server folder to scan
/// (<see cref="FolderPath"/>). Exactly one is set. Every source goes through the same queue so
/// each job runs in its own DI scope; a HuggingFace import used to run in a Task.Run against the
/// request's scoped services, racing their disposal.
/// </summary>
/// <param name="DatasetId">Target dataset.</param>
/// <param name="FilePath">Staged upload, deleted once the job has finished for good.</param>
/// <param name="HuggingFaceRequest">HuggingFace import parameters.</param>
/// <param name="FolderPath">Folder to scan. Never deleted: it is the caller's own folder.</param>
public sealed record IngestionJob(
    Guid DatasetId,
    string? FilePath,
    ImportHuggingFaceDatasetRequest? HuggingFaceRequest = null,
    string? FolderPath = null);
