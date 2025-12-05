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

    public async Task<IReadOnlyList<DatasetSummaryDto>> GetAllDatasetsAsync(int page = 0, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        string path = $"api/datasets?page={page}&pageSize={pageSize}";

        using HttpResponseMessage response = await _httpClient.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using Stream contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument doc = await JsonDocument.ParseAsync(contentStream, default, cancellationToken);

        if (!doc.RootElement.TryGetProperty("datasets", out JsonElement datasetsElement))
        {
            return Array.Empty<DatasetSummaryDto>();
        }

        List<DatasetSummaryDto>? datasets = datasetsElement.Deserialize<List<DatasetSummaryDto>>(SerializerOptions);
        return datasets ?? new List<DatasetSummaryDto>();
    }

    public async Task<DatasetDetailDto?> CreateDatasetAsync(CreateDatasetRequest request, CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response = await _httpClient.PostAsJsonAsync("api/datasets", request, SerializerOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DatasetDetailDto>(SerializerOptions, cancellationToken);
    }

    public async Task<bool> DeleteDatasetAsync(Guid datasetId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.DeleteAsync($"api/datasets/{datasetId}", cancellationToken);
        return response.IsSuccessStatusCode;
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

    public async Task<PageResponse<DatasetItemDto>?> GetDatasetItemsAsync(Guid datasetId, int pageSize = 100, string? cursor = null, string? huggingFaceAccessToken = null, CancellationToken cancellationToken = default)
    {
        StringBuilder pathBuilder = new StringBuilder($"api/datasets/{datasetId}/items?pageSize={pageSize}");
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            pathBuilder.Append("&cursor=");
            pathBuilder.Append(Uri.EscapeDataString(cursor));
        }

        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, pathBuilder.ToString());

        if (!string.IsNullOrWhiteSpace(huggingFaceAccessToken))
        {
            request.Headers.Add("X-HF-Access-Token", huggingFaceAccessToken);
        }

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<PageResponse<DatasetItemDto>>(SerializerOptions, cancellationToken);
    }

    public async Task<bool> ImportFromHuggingFaceAsync(Guid datasetId, ImportHuggingFaceDatasetRequest request, CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
            $"api/datasets/{datasetId}/import-huggingface",
            request,
            SerializerOptions,
            cancellationToken);

        return response.IsSuccessStatusCode;
    }

    public async Task<HuggingFaceDiscoveryResponse?> DiscoverHuggingFaceDatasetAsync(HuggingFaceDiscoveryRequest request, CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
            "api/datasets/huggingface/discover",
            request,
            SerializerOptions,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<HuggingFaceDiscoveryResponse>(SerializerOptions, cancellationToken);
    }
}
