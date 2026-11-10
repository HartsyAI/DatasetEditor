namespace DatasetStudio.APIBackend.Services.DatasetManagement;

/// <summary>What happened to an edit applied inside a dataset's lock.</summary>
public enum ItemEditStatus
{
    /// <summary>The edit was applied and written.</summary>
    Updated,

    /// <summary>The dataset has no item with that id; nothing was written.</summary>
    NotFound,

    /// <summary>The precondition (the caller's <c>If-Match</c>) refused the current item; nothing was written.</summary>
    PreconditionFailed
}
