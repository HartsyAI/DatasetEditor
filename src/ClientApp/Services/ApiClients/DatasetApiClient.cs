using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DatasetStudio.DTO.Common;
using DatasetStudio.DTO.Datasets;
using DatasetStudio.DTO.Items;

namespace DatasetStudio.ClientApp.Services.ApiClients;

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

    public async Task<DatasetDto?> CreateDatasetAsync(CreateDatasetRequest request, CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response = await _httpClient.PostAsJsonAsync("api/datasets", request, SerializerOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DatasetDto>(SerializerOptions, cancellationToken);
    }

    public async Task<bool> DeleteDatasetAsync(Guid datasetId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.DeleteAsync($"api/datasets/{datasetId}", cancellationToken);
        return response.IsSuccessStatusCode;
    }

    /// <summary>Everyone the dataset is shared with. Throws when the caller may not manage its shares.</summary>
    public async Task<List<DatasetShareDto>> GetSharesAsync(Guid datasetId, CancellationToken cancellationToken = default)
    {
        List<DatasetShareDto>? shares = await _httpClient.GetFromJsonAsync<List<DatasetShareDto>>($"api/datasets/{datasetId}/shares", SerializerOptions, cancellationToken);
        return shares ?? new List<DatasetShareDto>();
    }

    /// <summary>
    /// Shares the dataset with an account, or changes its share. On refusal the server's reason
    /// ("No active account has that username or email.", "Your own access is read-only…") comes
    /// back as the error, for the dialog to show as is.
    /// </summary>
    public async Task<(DatasetShareDto? Share, string? Error)> ShareDatasetAsync(Guid datasetId, ShareDatasetRequest request, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PutAsJsonAsync($"api/datasets/{datasetId}/shares", request, SerializerOptions, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return (await response.Content.ReadFromJsonAsync<DatasetShareDto>(SerializerOptions, cancellationToken), null);
        }
        string? error = null;
        try
        {
            Dictionary<string, string>? body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(SerializerOptions, cancellationToken);
            error = body?.GetValueOrDefault("error");
        }
        catch (System.Text.Json.JsonException)
        {
            // A bare status (403 from Forbid()) has no body.
        }
        return (null, error ?? $"Sharing failed (HTTP {(int)response.StatusCode}).");
    }

    /// <summary>Ends a user's share. False when it was already gone or not the caller's to end.</summary>
    public async Task<bool> RevokeShareAsync(Guid datasetId, Guid userId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.DeleteAsync($"api/datasets/{datasetId}/shares/{userId}", cancellationToken);
        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// Patches one item. Goes through this client so the cookie handler is attached; a raw
    /// HttpClient hits the client origin and every edit fails (audit U-2). With
    /// <paramref name="ifMatchVersion"/> (the item's <see cref="DatasetItemDto.Version"/>) the
    /// server refuses the edit with 412 if the item changed since it was loaded (audit E-4).
    /// </summary>
    public async Task<ItemUpdateResult> UpdateItemAsync(Guid itemId, UpdateItemRequest request, string? ifMatchVersion = null, CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage message = new(HttpMethod.Patch, $"api/items/{itemId}")
        {
            Content = JsonContent.Create(request, options: SerializerOptions)
        };
        if (!string.IsNullOrEmpty(ifMatchVersion))
        {
            message.Headers.TryAddWithoutValidation("If-Match", $"\"{ifMatchVersion}\"");
        }

        using HttpResponseMessage response = await _httpClient.SendAsync(message, cancellationToken);
        ItemUpdateOutcome outcome = response.IsSuccessStatusCode ? ItemUpdateOutcome.Saved
            : response.StatusCode == HttpStatusCode.PreconditionFailed ? ItemUpdateOutcome.Conflict
            : ItemUpdateOutcome.Failed;
        ItemEditResponse? body = outcome == ItemUpdateOutcome.Failed ? null : await TryReadAsync<ItemEditResponse>(response, cancellationToken);
        return new ItemUpdateResult(outcome, body);
    }

    /// <summary>
    /// Patches many items. Returns the server's report (how many were edited, which were skipped
    /// and why, audit E-1), or null when the request failed.
    /// </summary>
    public async Task<BulkUpdateItemsResponse?> BulkUpdateItemsAsync(BulkUpdateItemsRequest request, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PatchAsJsonAsync("api/items/bulk", request, SerializerOptions, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await TryReadAsync<BulkUpdateItemsResponse>(response, cancellationToken) ?? new BulkUpdateItemsResponse();
    }

    /// <summary>Reads a JSON body, or null when there is none or it isn't the expected shape.</summary>
    private static async Task<T?> TryReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken) where T : class
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            // No content type (an empty body).
            return null;
        }
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

    public Task<DatasetDto?> GetDatasetAsync(Guid datasetId, CancellationToken cancellationToken = default)
    {
        return _httpClient.GetFromJsonAsync<DatasetDto>($"api/datasets/{datasetId}", SerializerOptions, cancellationToken);
    }

    public async Task<PageResponse<DatasetItemDto>?> GetDatasetItemsAsync(Guid datasetId, int pageSize = 100, string? cursor = null, string? huggingFaceAccessToken = null, CancellationToken cancellationToken = default, FilterRequest? filter = null)
    {
        StringBuilder pathBuilder = new StringBuilder($"api/datasets/{datasetId}/items?pageSize={pageSize}");
        // The server applies the filter (audit G-2); the same parameter names are parsed there.
        pathBuilder.Append(FilterQueryString.Build(filter));
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

    public async Task<bool> ImportFromFolderAsync(Guid datasetId, ImportFolderRequest request, CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
            $"api/datasets/{datasetId}/import-folder",
            request,
            SerializerOptions,
            cancellationToken);

        return response.IsSuccessStatusCode;
    }

    public async Task<bool> ImportFromS3Async(Guid datasetId, ImportS3DatasetRequest request, CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
            $"api/datasets/{datasetId}/import-s3",
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
