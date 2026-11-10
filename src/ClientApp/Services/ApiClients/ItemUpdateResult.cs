using DatasetStudio.DTO.Items;

namespace DatasetStudio.ClientApp.Services.ApiClients;

/// <summary>
/// The result of <see cref="DatasetApiClient.UpdateItemAsync"/>: the outcome and, for Saved and
/// Conflict, the server's view of the item (its version and the fields it holds).
/// </summary>
/// <param name="Outcome">How the edit ended.</param>
/// <param name="Item">The server's reply body, when it sent one.</param>
public sealed record ItemUpdateResult(ItemUpdateOutcome Outcome, ItemEditResponse? Item);
