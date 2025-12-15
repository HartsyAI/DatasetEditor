using DatasetStudio.DTO.Common;
using DatasetStudio.DTO.Datasets;
using DatasetStudio.Core.DomainModels;

namespace DatasetStudio.APIBackend.Services.Storage;

/// <summary>
/// Service for reading and writing dataset items to Parquet files.
/// Provides high-performance columnar storage for large datasets.
/// </summary>
public interface IParquetDataService
{
    /// <summary>
    /// Writes dataset items to a Parquet file, creating or overwriting the file
    /// </summary>
    /// <param name="filePath">Path to the Parquet file</param>
    /// <param name="items">Items to write</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task WriteAsync(string filePath, IEnumerable<DatasetItemDto> items, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends dataset items to an existing Parquet file
    /// </summary>
    /// <param name="filePath">Path to the Parquet file</param>
    /// <param name="items">Items to append</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task AppendAsync(string filePath, IEnumerable<DatasetItemDto> items, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads dataset items from a Parquet file with pagination
    /// </summary>
    /// <param name="filePath">Path to the Parquet file</param>
    /// <param name="offset">Number of items to skip</param>
    /// <param name="limit">Maximum number of items to return</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Paged result containing items and total count</returns>
    Task<PagedResult<DatasetItemDto>> ReadAsync(string filePath, int offset, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the total count of items in a Parquet file
    /// </summary>
    /// <param name="filePath">Path to the Parquet file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Total number of items</returns>
    Task<long> GetCountAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a single item by ID from a Parquet file
    /// </summary>
    /// <param name="filePath">Path to the Parquet file</param>
    /// <param name="itemId">Item ID to find</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The item if found, null otherwise</returns>
    Task<DatasetItemDto?> ReadItemAsync(string filePath, string itemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a single item in a Parquet file
    /// Note: This requires reading all items, updating one, and rewriting the file
    /// </summary>
    /// <param name="filePath">Path to the Parquet file</param>
    /// <param name="item">Item to update (matched by Id)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UpdateItemAsync(string filePath, DatasetItemDto item, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a single item from a Parquet file
    /// Note: This requires reading all items, filtering one out, and rewriting the file
    /// </summary>
    /// <param name="filePath">Path to the Parquet file</param>
    /// <param name="itemId">ID of item to delete</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task DeleteItemAsync(string filePath, string itemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches items in a Parquet file by query string (title, description, tags)
    /// </summary>
    /// <param name="filePath">Path to the Parquet file</param>
    /// <param name="query">Search query</param>
    /// <param name="offset">Number of items to skip</param>
    /// <param name="limit">Maximum number of items to return</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Paged result of matching items</returns>
    Task<PagedResult<DatasetItemDto>> SearchAsync(string filePath, string query, int offset, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Filters items by tag
    /// </summary>
    /// <param name="filePath">Path to the Parquet file</param>
    /// <param name="tag">Tag to filter by</param>
    /// <param name="offset">Number of items to skip</param>
    /// <param name="limit">Maximum number of items to return</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Paged result of matching items</returns>
    Task<PagedResult<DatasetItemDto>> GetByTagAsync(string filePath, string tag, int offset, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets favorite items
    /// </summary>
    /// <param name="filePath">Path to the Parquet file</param>
    /// <param name="offset">Number of items to skip</param>
    /// <param name="limit">Maximum number of items to return</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Paged result of favorite items</returns>
    Task<PagedResult<DatasetItemDto>> GetFavoritesAsync(string filePath, int offset, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a Parquet file exists and is valid
    /// </summary>
    /// <param name="filePath">Path to check</param>
    /// <returns>True if file exists and is a valid Parquet file</returns>
    bool Exists(string filePath);

    /// <summary>
    /// Deletes a Parquet file
    /// </summary>
    /// <param name="filePath">Path to the Parquet file</param>
    void Delete(string filePath);
}
