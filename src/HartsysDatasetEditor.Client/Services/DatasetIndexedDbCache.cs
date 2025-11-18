using HartsysDatasetEditor.Client.Services.JsInterop;
using HartsysDatasetEditor.Contracts.Datasets;
using HartsysDatasetEditor.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace HartsysDatasetEditor.Client.Services;

/// <summary>
/// IndexedDB cache for dataset pages with full persistence via Dexie.js
/// </summary>
public sealed class DatasetIndexedDbCache
{
    private readonly IndexedDbInterop _indexedDb;
    private readonly ILogger<DatasetIndexedDbCache> _logger;
    private readonly Dictionary<string, int> _cursorToPageMap = new();
    private int _currentPage = 0;

    public DatasetIndexedDbCache(IndexedDbInterop indexedDb, ILogger<DatasetIndexedDbCache> logger)
    {
        _indexedDb = indexedDb ?? throw new ArgumentNullException(nameof(indexedDb));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SavePageAsync(Guid datasetId, string? cursor, IReadOnlyList<DatasetItemDto> items, CancellationToken cancellationToken = default)
    {
        try
        {
            // Map cursor to page number
            if (!string.IsNullOrEmpty(cursor))
            {
                _cursorToPageMap[cursor] = _currentPage;
            }

            _logger.LogDebug("💾 Saving {Count} items to IndexedDB for dataset {DatasetId} (page={Page})", 
                items.Count, datasetId, _currentPage);

            bool success = await _indexedDb.SavePageAsync(
                datasetId.ToString(), 
                _currentPage, 
                items.ToList());

            if (success)
            {
                Logs.Info($"[CACHE SAVED] Page {_currentPage} with {items.Count} items");
                _currentPage++;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save page to IndexedDB");
        }
    }

    public async Task<IReadOnlyList<DatasetItemDto>?> TryLoadPageAsync(Guid datasetId, string? cursor, CancellationToken cancellationToken = default)
    {
        try
        {
            // Get page number from cursor
            int page = 0;
            if (!string.IsNullOrEmpty(cursor) && _cursorToPageMap.TryGetValue(cursor, out int mappedPage))
            {
                page = mappedPage;
            }

            _logger.LogDebug("🔍 Looking up cached page {Page} for dataset {DatasetId}", page, datasetId);

            CachedPage? cachedPage = await _indexedDb.GetPageAsync(datasetId.ToString(), page);

            if (cachedPage != null && cachedPage.Items.Any())
            {
                Logs.Info($"[CACHE HIT] Page {page} loaded from IndexedDB ({cachedPage.Items.Count} items)");
                return cachedPage.Items;
            }

            Logs.Info($"[CACHE MISS] Page {page} not found in IndexedDB");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load page from IndexedDB");
            return null;
        }
    }

    public async Task ClearAsync(Guid datasetId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("🧹 Clearing IndexedDB cache for dataset {DatasetId}", datasetId);

            bool success = await _indexedDb.ClearDatasetAsync(datasetId.ToString());

            if (success)
            {
                _cursorToPageMap.Clear();
                _currentPage = 0;
                Logs.Info($"[CACHE CLEARED] Dataset {datasetId}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear IndexedDB cache");
        }
    }
}
