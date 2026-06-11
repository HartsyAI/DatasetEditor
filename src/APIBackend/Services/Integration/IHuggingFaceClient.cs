using DatasetStudio.APIBackend.Models;

namespace DatasetStudio.APIBackend.Services.Integration;

/// <summary>
/// Client for interacting with HuggingFace Hub API to fetch dataset metadata and files.
/// </summary>
public interface IHuggingFaceClient
{
    /// <summary>
    /// Validates that a dataset exists on HuggingFace Hub and fetches its metadata.
    /// </summary>
    /// <param name="repository">Repository name (e.g., "username/dataset-name")</param>
    /// <param name="revision">Optional revision (branch/tag/commit). Defaults to "main".</param>
    /// <param name="accessToken">Optional HuggingFace access token for private datasets</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dataset metadata if found, null otherwise</returns>
    Task<HuggingFaceDatasetInfo?> GetDatasetInfoAsync(
        string repository,
        string? revision = null,
        string? accessToken = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a dataset file from HuggingFace Hub.
    /// </summary>
    /// <param name="repository">Repository name</param>
    /// <param name="fileName">File name to download (e.g., "train.parquet")</param>
    /// <param name="destinationPath">Local path to save the file</param>
    /// <param name="revision">Optional revision</param>
    /// <param name="accessToken">Optional access token</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task DownloadFileAsync(
        string repository,
        string fileName,
        string destinationPath,
        string? revision = null,
        string? accessToken = null,
        CancellationToken cancellationToken = default);
}

