using Microsoft.AspNetCore.Components;

namespace DatasetStudio.ClientApp.Features.Datasets.Components;

/// <summary>
/// The in-progress block shown while a create-dataset form talks to the server (DESIGN.md §4.1).
/// <c>DatasetUploader</c> had four copies of it, one with a percentage computed from fixed steps
/// that did not track real progress. This one is indeterminate and says which step is running; the
/// viewer's status band reports ingestion progress once the form hands over.
/// </summary>
public partial class IngestionProgressPanel
{
    /// <summary>What is happening, for example "Uploading".</summary>
    [Parameter, EditorRequired] public string Title { get; set; } = string.Empty;

    /// <summary>The current step, for example "Creating the dataset".</summary>
    [Parameter] public string Status { get; set; } = string.Empty;
}
