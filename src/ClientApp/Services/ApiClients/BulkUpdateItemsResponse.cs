namespace DatasetStudio.ClientApp.Services.ApiClients;

/// <summary>
/// Body returned by <c>PATCH api/items/bulk</c>. Mirrors the anonymous <c>{ updatedCount }</c>
/// object the endpoint writes; kept as a named type so the client can deserialise it.
/// </summary>
public sealed record BulkUpdateItemsResponse(int UpdatedCount);
