using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DatasetStudio.ClientApp.Services.ApiClients;
using DatasetStudio.ClientApp.Services.StateManagement;
using DatasetStudio.DTO.Common;
using DatasetStudio.DTO.Datasets;
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
    private readonly DatasetIndexedDbCache _indexedDbCache;
    private readonly ApiKeyState _apiKeyState;
    private readonly ILogger<DatasetCacheService> _logger;
    private readonly SemaphoreSlim _pageLock = new(1, 1);
    private bool _isIndexedDbEnabled = false;
    private bool _isBuffering;
    private const int MaxBufferedItems = 100_000;
    private int _windowStartIndex = 0;

    public Guid? CurrentDatasetId { get; private set; }
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
        DatasetIndexedDbCache indexedDbCache,
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
    /// Loads the dataset metadata and first page of items from the API.
    /// TODO: Add prefetch + background page streaming for near-infinite scrolling (see checklist Phase C).
    /// </summary>
    public async Task LoadFirstPageAsync(Guid datasetId, CancellationToken cancellationToken = default)
    {
        _datasetState.SetLoading(true);

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

            Dataset mappedDataset = MapDataset(dataset);
            List<IDatasetItem> items = MapItems(dataset.Id, page?.Items ?? Array.Empty<DatasetItemDto>());

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
            _datasetState.SetError("Failed to load dataset from API");
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

            List<IDatasetItem> newItems = MapItems(CurrentDatasetId.Value, page.Items);

            List<IDatasetItem> currentWindow = _datasetState.Items;
            List<IDatasetItem> combined = new(currentWindow.Count + newItems.Count);
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

    public async Task<bool> LoadPreviousPageAsync(CancellationToken cancellationToken = default, bool suppressBufferingNotification = false)
    {
        if (CurrentDatasetId == null || _windowStartIndex <= 0)
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
            const int pageSize = 100;

            int prevStartIndex = _windowStartIndex - pageSize;
            int effectivePageSize = pageSize;
            if (prevStartIndex < 0)
            {
                effectivePageSize += prevStartIndex; // prevStartIndex is negative here
                prevStartIndex = 0;
            }

            if (effectivePageSize <= 0)
            {
                return false;
            }

            string? prevCursor = prevStartIndex == 0 ? null : prevStartIndex.ToString();

            PageResponse<DatasetItemDto>? page = await FetchPageAsync(CurrentDatasetId.Value, effectivePageSize, prevCursor, CurrentDatasetDetail, cancellationToken).ConfigureAwait(false);
            if (page == null || page.Items.Count == 0)
            {
                return false;
            }

            List<IDatasetItem> newItems = MapItems(CurrentDatasetId.Value, page.Items);

            List<IDatasetItem> currentWindow = _datasetState.Items;
            List<IDatasetItem> combined = new(newItems.Count + currentWindow.Count);
            combined.AddRange(newItems);
            combined.AddRange(currentWindow);

            if (combined.Count > MaxBufferedItems)
            {
                int overflow = combined.Count - MaxBufferedItems;
                if (overflow > 0)
                {
                    if (overflow > combined.Count)
                    {
                        overflow = combined.Count;
                    }

                    // For previous pages, evict from the end of the window
                    combined.RemoveRange(combined.Count - overflow, overflow);
                }
            }

            _windowStartIndex = prevStartIndex;
            _datasetState.SetItemsWindow(combined);
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

    public async Task EnsureBufferedAsync(int minimumCount, CancellationToken cancellationToken = default)
    {
        if (CurrentDatasetId == null)
        {
            return;
        }

        int effectiveMinimum = Math.Min(minimumCount, MaxBufferedItems);

        bool bufferingRaised = false;

        try
        {
            while (_datasetState.Items.Count < effectiveMinimum && HasMorePages)
            {
                if (!bufferingRaised)
                {
                    SetBuffering(true);
                    bufferingRaised = true;
                }

                bool loaded = await LoadNextPageAsync(cancellationToken, suppressBufferingNotification: true).ConfigureAwait(false);
                if (!loaded)
                {
                    break;
                }
            }
        }
        finally
        {
            if (bufferingRaised)
            {
                SetBuffering(false);
            }
        }
    }

    public async Task<DatasetDetailDto?> RefreshDatasetStatusAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentDatasetId is null)
        {
            return null;
        }

        DatasetDetailDto? detail = await _apiClient.GetDatasetAsync(CurrentDatasetId.Value, cancellationToken).ConfigureAwait(false);
        if (detail != null)
        {
            CurrentDatasetDetail = detail;
            OnDatasetDetailChanged?.Invoke();
        }

        return detail;
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
        if (_isIndexedDbEnabled)
        {
            IReadOnlyList<DatasetItemDto>? cachedItems = await _indexedDbCache.TryLoadPageAsync(datasetId, cursor, cancellationToken).ConfigureAwait(false);
            if (cachedItems != null)
            {
                // Cache hit - but we need to calculate the next cursor
                // Cursor format is the starting index as a string (e.g., "100", "200")
                int currentIndex = string.IsNullOrEmpty(cursor) ? 0 : int.Parse(cursor);
                int nextIndex = currentIndex + cachedItems.Count;
                
                // We don't know the total count from cache alone, so assume there might be more
                // The API will return null cursor when there's no more data
                string? nextCursor = nextIndex.ToString();
                
                return new PageResponse<DatasetItemDto>
                {
                    Items = cachedItems,
                    NextCursor = nextCursor
                };
            }
        }

        string? huggingFaceToken = null;
        if (datasetDetail != null && datasetDetail.SourceType == DatasetSourceType.HuggingFaceStreaming && datasetDetail.IsStreaming)
        {
            huggingFaceToken = _apiKeyState.GetToken(ApiKeyState.ProviderHuggingFace);
        }

        PageResponse<DatasetItemDto>? page = await _apiClient.GetDatasetItemsAsync(datasetId, pageSize, cursor, huggingFaceToken, cancellationToken).ConfigureAwait(false);
        if (_isIndexedDbEnabled && page?.Items.Count > 0)
        {
            await _indexedDbCache.SavePageAsync(datasetId, cursor, page.Items, cancellationToken).ConfigureAwait(false);
        }

        return page;
    }

    private static Dataset MapDataset(DatasetDetailDto dto) => new()
    {
        Id = dto.Id.ToString(),
        Name = dto.Name,
        Description = dto.Description ?? string.Empty,
        CreatedAt = dto.CreatedAt,
        UpdatedAt = dto.UpdatedAt,
        Modality = Modality.Image,
        TotalItems = dto.TotalItems > int.MaxValue ? int.MaxValue : (int)dto.TotalItems
    };

    private static List<IDatasetItem> MapItems(Guid datasetId, IReadOnlyList<DatasetItemDto> items)
    {
        string datasetIdString = datasetId.ToString();
        List<IDatasetItem> mapped = new(items.Count);

        foreach (DatasetItemDto item in items)
        {
            string primaryImage = item.ImageUrl ?? item.ThumbnailUrl ?? string.Empty;
            if (string.IsNullOrWhiteSpace(primaryImage))
            {
                continue;
            }

            ImageItem imageItem = new()
            {
                Id = item.Id.ToString(),
                DatasetId = datasetIdString,
                Title = string.IsNullOrWhiteSpace(item.Title) ? item.ExternalId : item.Title,
                Description = item.Description ?? string.Empty,
                SourcePath = primaryImage,
                ImageUrl = item.ImageUrl ?? primaryImage,
                ThumbnailUrl = item.ThumbnailUrl ?? item.ImageUrl ?? primaryImage,
                Width = item.Width,
                Height = item.Height,
                Tags = new List<string>(item.Tags),
                IsFavorite = item.IsFavorite,
                Metadata = new Dictionary<string, string>(item.Metadata),
                CreatedAt = item.CreatedAt,
                UpdatedAt = item.UpdatedAt
            };

            mapped.Add(imageItem);
        }

        return mapped;
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
