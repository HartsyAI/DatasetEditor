using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HartsysDatasetEditor.Client.Services.Api;
using HartsysDatasetEditor.Client.Services.StateManagement;
using HartsysDatasetEditor.Contracts.Common;
using HartsysDatasetEditor.Contracts.Datasets;
using HartsysDatasetEditor.Core.Enums;
using HartsysDatasetEditor.Core.Interfaces;
using HartsysDatasetEditor.Core.Models;
using Microsoft.Extensions.Logging;

namespace HartsysDatasetEditor.Client.Services;

/// <summary>
/// Coordinates client-side dataset loading via the API and keeps <see cref="DatasetState"/> in sync.
/// TODO: Extend to manage paged caches/IndexedDB per docs/architecture.md section 3.1.
/// </summary>
public sealed class DatasetCacheService : IDisposable
{
    private readonly DatasetApiClient _apiClient;
    private readonly DatasetState _datasetState;
    private readonly DatasetIndexedDbCache _indexedDbCache;
    private readonly ILogger<DatasetCacheService> _logger;
    private readonly SemaphoreSlim _pageLock = new(1, 1);
    private bool _isIndexedDbEnabled = true;
    private bool _isBuffering;

    public Guid? CurrentDatasetId { get; private set; }
    public string? NextCursor { get; private set; }
    public DatasetDetailDto? CurrentDatasetDetail { get; private set; }

    public bool HasMorePages => !string.IsNullOrWhiteSpace(NextCursor);
    public bool IsIndexedDbEnabled => _isIndexedDbEnabled;
    public bool IsBuffering => _isBuffering;

    public event Action? OnDatasetDetailChanged;
    public event Action<bool>? OnBufferingStateChanged;

    public DatasetCacheService(
        DatasetApiClient apiClient,
        DatasetState datasetState,
        DatasetIndexedDbCache indexedDbCache,
        ILogger<DatasetCacheService> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _datasetState = datasetState ?? throw new ArgumentNullException(nameof(datasetState));
        _indexedDbCache = indexedDbCache ?? throw new ArgumentNullException(nameof(indexedDbCache));
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

            PageResponse<DatasetItemDto>? page = await FetchPageAsync(datasetId, pageSize: 100, cursor: null, cancellationToken).ConfigureAwait(false);

            Dataset mappedDataset = MapDataset(dataset);
            List<IDatasetItem> items = MapItems(dataset.Id, page?.Items ?? Array.Empty<DatasetItemDto>());

            _datasetState.LoadDataset(mappedDataset, items);
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
            PageResponse<DatasetItemDto>? page = await FetchPageAsync(CurrentDatasetId.Value, 100, NextCursor, cancellationToken).ConfigureAwait(false);
            if (page == null || page.Items.Count == 0)
            {
                NextCursor = null;
                return false;
            }

            List<IDatasetItem> items = MapItems(CurrentDatasetId.Value, page.Items);
            _datasetState.AppendItems(items);
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

    public async Task EnsureBufferedAsync(int minimumCount, CancellationToken cancellationToken = default)
    {
        if (CurrentDatasetId == null)
        {
            return;
        }

        bool bufferingRaised = false;

        try
        {
            while (_datasetState.Items.Count < minimumCount && HasMorePages)
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

    private async Task<PageResponse<DatasetItemDto>?> FetchPageAsync(Guid datasetId, int pageSize, string? cursor, CancellationToken cancellationToken)
    {
        if (_isIndexedDbEnabled)
        {
            IReadOnlyList<DatasetItemDto>? cachedItems = await _indexedDbCache.TryLoadPageAsync(datasetId, cursor, cancellationToken).ConfigureAwait(false);
            if (cachedItems != null)
            {
                return new PageResponse<DatasetItemDto>
                {
                    Items = cachedItems,
                    NextCursor = null
                };
            }
        }

        PageResponse<DatasetItemDto>? page = await _apiClient.GetDatasetItemsAsync(datasetId, pageSize, cursor, cancellationToken).ConfigureAwait(false);
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
            var imageItem = new ImageItem
            {
                Id = item.Id.ToString(),
                DatasetId = datasetIdString,
                Title = string.IsNullOrWhiteSpace(item.Title) ? item.ExternalId : item.Title,
                Description = item.Description ?? string.Empty,
                SourcePath = item.ImageUrl ?? item.ThumbnailUrl ?? string.Empty,
                ImageUrl = item.ImageUrl ?? string.Empty,
                ThumbnailUrl = item.ThumbnailUrl ?? item.ImageUrl ?? string.Empty,
                Width = item.Width,
                Height = item.Height,
                Metadata = new Dictionary<string, string>(item.Metadata)
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
