using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HartsysDatasetEditor.Contracts.Common;
using HartsysDatasetEditor.Contracts.Datasets;

namespace HartsysDatasetEditor.Client.Services.Api;

/// <summary>
/// Thin wrapper over <see cref="HttpClient"/> for calling the Dataset API endpoints.
/// </summary>
public sealed class DatasetApiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public DatasetApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<DatasetDetailDto?> CreateDatasetAsync(CreateDatasetRequest request, CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response = await _httpClient.PostAsJsonAsync("api/datasets", request, SerializerOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DatasetDetailDto>(SerializerOptions, cancellationToken);
    }

    public async Task UploadDatasetAsync(Guid datasetId, Stream fileStream, string fileName, string? contentType = null, CancellationToken cancellationToken = default)
    {
        using MultipartFormDataContent form = new();
        var fileContent = new StreamContent(fileStream);
        string mediaType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType;
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        form.Add(fileContent, "file", fileName);

        HttpResponseMessage response = await _httpClient.PostAsync($"api/datasets/{datasetId}/upload", form, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public Task<DatasetDetailDto?> GetDatasetAsync(Guid datasetId, CancellationToken cancellationToken = default)
    {
        return _httpClient.GetFromJsonAsync<DatasetDetailDto>($"api/datasets/{datasetId}", SerializerOptions, cancellationToken);
    }

    public Task<PageResponse<DatasetItemDto>?> GetDatasetItemsAsync(Guid datasetId, int pageSize = 100, string? cursor = null, CancellationToken cancellationToken = default)
    {
        var pathBuilder = new StringBuilder($"api/datasets/{datasetId}/items?pageSize={pageSize}");
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            pathBuilder.Append("&cursor=");
            pathBuilder.Append(Uri.EscapeDataString(cursor));
        }

        return _httpClient.GetFromJsonAsync<PageResponse<DatasetItemDto>>(pathBuilder.ToString(), SerializerOptions, cancellationToken);
    }
}
