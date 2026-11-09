using System.Net;
using DatasetStudio.ClientApp.Services.ApiClients;
using DatasetStudio.DTO.Datasets;
using Microsoft.AspNetCore.Components.Forms;

namespace DatasetStudio.ClientApp.Features.Datasets.Services;

/// <summary>
/// The steps every "create dataset" form shares: create the record, hand it its data, and, where a
/// decision depends on it, wait for the server to report a status. It replaces four copies of a
/// create → import → <c>Task.Delay</c> → load → navigate sequence in <c>DatasetUploader</c>, whose
/// fixed delays (0.5–5 s) were either too short (the user was told to "check the dashboard later")
/// or wasted time (audit U-15). Forms now navigate to the viewer as soon as the server accepts the
/// job; the viewer's status band polls ingestion from there.
/// </summary>
public sealed class IngestionWorkflowService
{
    /// <summary>Largest file the upload endpoint accepts (matches the API's request limit).</summary>
    public const long MaxUploadBytes = 5L * 1024 * 1024 * 1024;

    /// <summary>File types the server can parse; anything else fails after the upload.</summary>
    public const string AcceptedUploadExtensions = ".csv,.tsv,.json,.jsonl,.ndjson,.zip";

    private readonly DatasetApiClient _api;

    /// <summary>Creates the service.</summary>
    /// <param name="api">The typed API client.</param>
    public IngestionWorkflowService(DatasetApiClient api)
    {
        _api = api;
    }

    /// <summary>How often <see cref="WaitForStatusAsync"/> asks the server; shortened in tests.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Creates the dataset record that an upload or import then fills.</summary>
    /// <exception cref="InvalidOperationException">The server returned no dataset.</exception>
    public async Task<DatasetDetailDto> CreateAsync(string name, string? description, bool isPublic, CancellationToken cancellationToken = default)
    {
        DatasetDetailDto? dataset = await _api.CreateDatasetAsync(
            new CreateDatasetRequest(name, description, IsPublic: isPublic), cancellationToken);
        return dataset ?? throw new InvalidOperationException("The server didn't return the new dataset.");
    }

    /// <summary>
    /// Streams a browser file to the upload endpoint. The uploader used to copy the whole file into
    /// a <c>MemoryStream</c> first, capped at <see cref="int.MaxValue"/> bytes, so files over 2 GB
    /// failed despite the 5 GB limit (audit U-14).
    /// </summary>
    public async Task UploadAsync(Guid datasetId, IBrowserFile file, CancellationToken cancellationToken = default)
    {
        await using Stream stream = file.OpenReadStream(MaxUploadBytes, cancellationToken);
        string contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
        await _api.UploadDatasetAsync(datasetId, stream, file.Name, contentType, cancellationToken);
    }

    /// <summary>
    /// Polls the dataset until <paramref name="isSettled"/> returns true or <paramref name="timeout"/>
    /// passes, and returns the last status read (null if it could never be read).
    /// </summary>
    /// <param name="datasetId">The dataset to watch.</param>
    /// <param name="isSettled">When to stop, given the latest status.</param>
    /// <param name="timeout">Upper bound on the wait.</param>
    /// <param name="cancellationToken">Stops the wait early.</param>
    public async Task<DatasetDetailDto?> WaitForStatusAsync(
        Guid datasetId,
        Func<DatasetDetailDto, bool> isSettled,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        DatasetDetailDto? latest = null;
        while (true)
        {
            latest = await _api.GetDatasetAsync(datasetId, cancellationToken) ?? latest;
            if (latest is not null && isSettled(latest))
            {
                return latest;
            }
            if (DateTime.UtcNow >= deadline)
            {
                return latest;
            }
            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    /// <summary>Deletes a dataset that an abandoned import created.</summary>
    public Task<bool> DeleteAsync(Guid datasetId, CancellationToken cancellationToken = default)
    {
        return _api.DeleteDatasetAsync(datasetId, cancellationToken);
    }

    /// <summary>
    /// The sentence a form shows when a step fails. The old helper named a hard-coded
    /// <c>https://localhost:7221</c> origin that this app has never used.
    /// </summary>
    public static string DescribeFailure(Exception exception)
    {
        return exception switch
        {
            HttpRequestException { StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized }
                => "You don't have permission to do that. Log in again and retry.",
            HttpRequestException { StatusCode: HttpStatusCode.RequestEntityTooLarge }
                => "The file is larger than the server accepts.",
            HttpRequestException { StatusCode: HttpStatusCode.BadRequest }
                => "The server rejected the request. Check the values and try again.",
            // Another request changed the same dataset field first (audit D-4).
            HttpRequestException { StatusCode: HttpStatusCode.Conflict }
                => "The dataset was changed by someone else. Reload it and try again.",
            HttpRequestException { StatusCode: not null } http
                => $"The server returned an error (HTTP {(int)http.StatusCode!.Value}). Retry, or check the API logs.",
            HttpRequestException => "The server didn't respond. Check that the API is running, then retry.",
            OperationCanceledException => "Cancelled.",
            _ => exception.Message
        };
    }
}
