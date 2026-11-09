using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DatasetStudio.ClientApp.Services.ApiClients;
using DatasetStudio.ClientApp.Services.StateManagement;
using DatasetStudio.ClientApp.Services.Caching;
using DatasetStudio.DTO.Common;
using DatasetStudio.DTO.Datasets;
using DatasetStudio.DTO.Items;
using DatasetStudio.Core.Enumerations;
using DatasetStudio.Core.Abstractions;
using DatasetStudio.Core.DomainModels;
using Microsoft.Extensions.Logging;

namespace DatasetStudio.ClientApp.Features.Datasets.Services;

/// <summary>
/// Coordinates client-side dataset loading via the API and keeps <see cref="DatasetState"/> in sync.
/// TODO: Extend to manage paged caches/IndexedDB per docs/architecture.md section 3.1.
/// </summary>
public sealed class DatasetCacheService : IDisposable
{
    private readonly DatasetApiClient _apiClient;
    private readonly DatasetState _datasetState;
    private readonly IndexedDbCache _indexedDbCache;
    private readonly ApiKeyState _apiKeyState;
    private readonly ILogger<DatasetCacheService> _logger;
    private readonly SemaphoreSlim _pageLock = new(1, 1);
    private bool _isIndexedDbEnabled = false;
    private bool _isBuffering;
    private const int MaxBufferedItems = 100_000;
    private int _windowStartIndex = 0;

    public Guid? CurrentDatasetId { get; private set; }

    /// <summary>
    /// The filter every page request carries, or null. The server filters (audit U-6), so the
    /// loaded items are the matches.
    /// </summary>
    public FilterRequest? ActiveFilter { get; private set; }
    public string? NextCursor { get; private set; }
    public DatasetDetailDto? CurrentDatasetDetail { get; private set; }

    public bool HasMorePages => !string.IsNullOrWhiteSpace(NextCursor);
    public bool HasPreviousPages => _windowStartIndex > 0;
    public bool IsIndexedDbEnabled => _isIndexedDbEnabled;
    public bool IsBuffering => _isBuffering;
    public int WindowStartIndex => _windowStartIndex;

    public event Action? OnDatasetDetailChanged;
    public event Action<bool>? OnBufferingStateChanged;

    public DatasetCacheService(
        DatasetApiClient apiClient,
        DatasetState datasetState,
        IndexedDbCache indexedDbCache,
        ApiKeyState apiKeyState,
        ILogger<DatasetCacheService> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _datasetState = datasetState ?? throw new ArgumentNullException(nameof(datasetState));
        _indexedDbCache = indexedDbCache ?? throw new ArgumentNullException(nameof(indexedDbCache));
        _apiKeyState = apiKeyState ?? throw new ArgumentNullException(nameof(apiKeyState));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Turns a first-page load failure into the sentence the viewer's error state shows. It used to
    /// say "Failed to load dataset from API" for everything, including a dataset id that doesn't exist.
    /// </summary>
    /// <param name="exception">The failure thrown while loading.</param>
    public static string DescribeLoadFailure(Exception exception)
    {
        // GetFromJsonAsync throws HttpRequestException for a 404/403 response as well as for a
        // network failure; StatusCode is what tells them apart.
        return exception switch
        {
            InvalidOperationException => "This dataset doesn't exist, or you don't have access to it.",
            HttpRequestException { StatusCode: System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Forbidden }
                => "This dataset doesn't exist, or you don't have access to it.",
            HttpRequestException { StatusCode: not null } http
                => $"The server couldn't load this dataset (HTTP {(int)http.StatusCode!.Value}). Retry, or check the API logs.",
            HttpRequestException => "The server didn't respond. Check that the API is running, then retry.",
            OperationCanceledException => "Loading was cancelled.",
            _ => "The dataset couldn't be loaded."
        };
    }

    /// <summary>
    /// Loads the dataset metadata and first page of items from the API. On failure the state's
    /// error is set (the viewer renders it inline) and the exception is rethrown to the caller.
    /// </summary>
    public Task LoadFirstPageAsync(Guid datasetId, CancellationToken cancellationToken = default)
    {
        return LoadFirstPageAsync(datasetId, showLoading: true, cancellationToken);
    }

    /// <param name="datasetId">The dataset to load.</param>
    /// <param name="showLoading">
    /// False for a filter change: the viewer keeps showing the grid (and the filter panel the user
    /// is typing in) while the matches load, instead of swapping to its loading state.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task LoadFirstPageAsync(Guid datasetId, bool showLoading, CancellationToken cancellationToken)
    {
        if (showLoading)
        {
            _datasetState.SetLoading(true);
        }

        try
        {
            await _pageLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            if (_isIndexedDbEnabled)
            {
                await _indexedDbCache.ClearAsync(datasetId, cancellationToken).ConfigureAwait(false);
            }

            DatasetDetailDto? dataset = await _apiClient.GetDatasetAsync(datasetId, cancellationToken).ConfigureAwait(false);
            if (dataset is null)
            {
                throw new InvalidOperationException("Dataset not found on server.");
            }

            PageResponse<DatasetItemDto>? page = await FetchPageAsync(datasetId, pageSize: 100, cursor: null, dataset, cancellationToken).ConfigureAwait(false);
            dataset = WithPageTotal(dataset, page);

            DatasetStudio.Core.DomainModels.Datasets.Dataset mappedDataset = MapDataset(dataset);
            List<DatasetItemDto> items = MapItems(dataset.Id, page?.Items ?? Array.Empty<DatasetItemDto>());

            _datasetState.LoadDataset(mappedDataset, items);
            _windowStartIndex = 0;
            CurrentDatasetId = datasetId;
            NextCursor = page?.NextCursor;
            CurrentDatasetDetail = dataset;
            OnDatasetDetailChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load dataset {DatasetId} from API", datasetId);
            _datasetState.SetError(DescribeLoadFailure(ex));
            throw;
        }
        finally
        {
            _pageLock.Release();
        }
    }

    public async Task<bool> LoadNextPageAsync(CancellationToken cancellationToken = default, bool suppressBufferingNotification = false)
    {
        if (CurrentDatasetId == null || string.IsNullOrWhiteSpace(NextCursor))
        {
            return false;
        }

        bool bufferingRaised = false;
        if (!suppressBufferingNotification)
        {
            SetBuffering(true);
            bufferingRaised = true;
        }

        await _pageLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            PageResponse<DatasetItemDto>? page = await FetchPageAsync(CurrentDatasetId.Value, 100, NextCursor, CurrentDatasetDetail, cancellationToken).ConfigureAwait(false);
            if (page == null || page.Items.Count == 0)
            {
                NextCursor = null;
                return false;
            }

            List<DatasetItemDto> newItems = MapItems(CurrentDatasetId.Value, page.Items);

            List<DatasetItemDto> currentWindow = _datasetState.Items;
            List<DatasetItemDto> combined = new(currentWindow.Count + newItems.Count);
            combined.AddRange(currentWindow);
            combined.AddRange(newItems);

            if (combined.Count > MaxBufferedItems)
            {
                int overflow = combined.Count - MaxBufferedItems;
                if (overflow > 0)
                {
                    if (overflow > combined.Count)
                    {
                        overflow = combined.Count;
                    }

                    combined.RemoveRange(0, overflow);
                    _windowStartIndex += overflow;
                }
            }

            _datasetState.SetItemsWindow(combined);
            NextCursor = page.NextCursor;
            return true;
        }
        finally
        {
            _pageLock.Release();
            if (bufferingRaised)
            {
                SetBuffering(false);
            }
        }
    }

    /// <summary>
    /// Sets the filter and reloads the open dataset from its first page. A request the server
    /// refuses (a filter the source can't apply) throws, and the previous filter stays active.
    /// </summary>
    public async Task ApplyFilterAsync(FilterRequest? filter, CancellationToken cancellationToken = default)
    {
        if (Equals(ActiveFilter, filter) || (ActiveFilter is not null && filter is not null && SameFilter(ActiveFilter, filter)))
        {
            return;
        }

        FilterRequest? previous = ActiveFilter;
        ActiveFilter = filter;
        if (CurrentDatasetId is not Guid datasetId)
        {
            return;
        }

        try
        {
            await LoadFirstPageAsync(datasetId, showLoading: false, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            ActiveFilter = previous;
            throw;
        }
    }

    private static bool SameFilter(FilterRequest a, FilterRequest b)
    {
        return FilterQueryString.Build(a) == FilterQueryString.Build(b);
    }

    public async Task<DatasetDetailDto?> RefreshDatasetStatusAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentDatasetId is null)
        {
            return null;
        }

        DatasetDetailDto? previous = CurrentDatasetDetail;
        DatasetDetailDto? detail = await _apiClient.GetDatasetAsync(CurrentDatasetId.Value, cancellationToken).ConfigureAwait(false);
        if (detail == null)
        {
            return null;
        }

        if (FinishedIngesting(previous, detail))
        {
            // The viewer opened while the import was still running, so its first page was empty or
            // partial and had no next cursor; status polling alone never showed the imported items.
            // Reload the first page now that ingestion is done (LoadFirstPageAsync publishes the new
            // detail). A failure is already reported through DatasetState by LoadFirstPageAsync.
            try
            {
                await LoadFirstPageAsync(CurrentDatasetId.Value, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Reloading dataset {DatasetId} after ingestion finished failed", CurrentDatasetId);
            }
            return CurrentDatasetDetail;
        }

        CurrentDatasetDetail = detail;
        OnDatasetDetailChanged?.Invoke();
        return detail;
    }

    /// <summary>
    /// Whether a status refresh shows ingestion finishing: the previous status was Pending or
    /// Processing and the new one is Completed. The viewer then reloads its first page.
    /// </summary>
    /// <param name="previous">The detail the viewer had.</param>
    /// <param name="current">The detail just read.</param>
    public static bool FinishedIngesting(DatasetDetailDto? previous, DatasetDetailDto current)
    {
        return previous is { Status: IngestionStatusDto.Pending or IngestionStatusDto.Processing }
            && current.Status == IngestionStatusDto.Completed;
    }

    public Task SetIndexedDbEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        _isIndexedDbEnabled = enabled;

        if (!enabled && CurrentDatasetId.HasValue)
        {
            return _indexedDbCache.ClearAsync(CurrentDatasetId.Value, cancellationToken);
        }

        return Task.CompletedTask;
    }

    private async Task<PageResponse<DatasetItemDto>?> FetchPageAsync(Guid datasetId, int pageSize, string? cursor, DatasetDetailDto? datasetDetail, CancellationToken cancellationToken)
    {
        // The page cache is keyed by cursor only, so it holds unfiltered pages; skip it while filtering.
        bool useIndexedDb = _isIndexedDbEnabled && ActiveFilter is null;
        if (useIndexedDb)
        {
            IReadOnlyList<DatasetItemDto>? cachedItems = await _indexedDbCache.TryLoadPageAsync(datasetId, cursor, cancellationToken).ConfigureAwait(false);
            if (cachedItems != null)
            {
                // We can only derive the next cursor arithmetically for the numeric
                // (HuggingFace offset) scheme. Opaque cursors (e.g. Parquet "shard:row")
                // can't be incremented here, so fall through to the API which returns
                // the authoritative NextCursor. (Avoids int.Parse crashing on them.)
                if (string.IsNullOrEmpty(cursor) || int.TryParse(cursor, out int currentIndex))
                {
                    int baseIndex = string.IsNullOrEmpty(cursor) ? 0 : int.Parse(cursor);
                    int nextIndex = baseIndex + cachedItems.Count;

                    // We don't know the total count from cache alone, so assume there
                    // might be more; the API returns a null cursor when there's no more.
                    return new PageResponse<DatasetItemDto>
                    {
                        Items = cachedItems,
                        NextCursor = nextIndex.ToString()
                    };
                }
            }
        }

        string? huggingFaceToken = null;
        if (datasetDetail != null && datasetDetail.SourceType == DatasetSourceType.HuggingFaceStreaming && datasetDetail.IsStreaming)
        {
            huggingFaceToken = _apiKeyState.GetToken(ApiKeyState.ProviderHuggingFace);
        }

        PageResponse<DatasetItemDto>? page = await _apiClient.GetDatasetItemsAsync(datasetId, pageSize, cursor, huggingFaceToken, cancellationToken, ActiveFilter).ConfigureAwait(false);
        if (useIndexedDb && page?.Items.Count > 0)
        {
            await _indexedDbCache.SavePageAsync(datasetId, cursor, page.Items, cancellationToken).ConfigureAwait(false);
        }

        return page;
    }

    private static DatasetStudio.Core.DomainModels.Datasets.Dataset MapDataset(DatasetDetailDto dto) => new()
    {
        Id = dto.Id.ToString(),
        Name = dto.Name,
        Description = dto.Description ?? string.Empty,
        CreatedAt = dto.CreatedAt,
        UpdatedAt = dto.UpdatedAt,
        Modality = Modality.Image,
        TotalItems = dto.TotalItems > int.MaxValue ? int.MaxValue : (int)dto.TotalItems
    };

    /// <summary>
    /// Takes the item total from the first page when the dataset record doesn't have one yet. For a
    /// HuggingFace streaming dataset the server learns the row count while serving that first page
    /// (it back-fills it then), but the record was read just before, so a new dataset showed
    /// "0 items" in the status band and no "loaded / total" until the next status refresh.
    /// </summary>
    /// <param name="dataset">The record read before the first page.</param>
    /// <param name="firstPage">The first page, whose TotalCount the server sets when known.</param>
    public static DatasetDetailDto WithPageTotal(DatasetDetailDto dataset, PageResponse<DatasetItemDto>? firstPage)
    {
        return dataset.TotalItems <= 0 && firstPage?.TotalCount is long total && total > 0
            ? dataset with { TotalItems = total }
            : dataset;
    }

    /// <summary>
    /// Copies a page's items into the buffer, keeping every item. Items with neither an image nor a
    /// thumbnail URL used to be dropped here, so the grid showed fewer items than the server reported
    /// and the count label drifted (audit U-30); the card now renders a "No image" placeholder.
    /// </summary>
    /// <param name="datasetId">The dataset the page belongs to.</param>
    /// <param name="items">The page's items.</param>
    public static List<DatasetItemDto> MapItems(Guid datasetId, IReadOnlyList<DatasetItemDto> items)
    {
        return new List<DatasetItemDto>(items);
    }

    private void SetBuffering(bool value)
    {
        if (_isBuffering == value)
        {
            return;
        }

        _isBuffering = value;
        OnBufferingStateChanged?.Invoke(value);
    }

    public void Dispose()
    {
        _pageLock.Dispose();
    }
}
