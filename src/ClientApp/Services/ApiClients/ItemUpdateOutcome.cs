namespace DatasetStudio.ClientApp.Services.ApiClients;

/// <summary>How an item edit ended.</summary>
public enum ItemUpdateOutcome
{
    /// <summary>The server saved the edit.</summary>
    Saved,

    /// <summary>
    /// The server refused it with 412: the item changed since the client loaded it (audit E-4).
    /// The item has been refreshed from the server's reply.
    /// </summary>
    Conflict,

    /// <summary>The request failed for any other reason.</summary>
    Failed
}
