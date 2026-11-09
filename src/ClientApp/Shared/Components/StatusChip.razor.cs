using DatasetStudio.ClientApp.Shared.Presentation;
using DatasetStudio.DTO.Datasets;
using Microsoft.AspNetCore.Components;

namespace DatasetStudio.ClientApp.Shared.Components;

/// <summary>
/// The one rendering of an ingestion status (DESIGN.md §5.3): fixed colour, icon and label per
/// status, so pages never choose a status colour themselves.
/// </summary>
public partial class StatusChip
{
    /// <summary>The ingestion status to show.</summary>
    [Parameter, EditorRequired] public IngestionStatusDto Status { get; set; }

    /// <summary>Extra CSS classes for spacing in the parent layout.</summary>
    [Parameter] public string? Class { get; set; }

    private StatusPresentation _presentation = StatusPresentation.For(IngestionStatusDto.Pending);

    /// <summary>Recomputes the presentation when the status changes.</summary>
    protected override void OnParametersSet()
    {
        _presentation = StatusPresentation.For(Status);
    }
}
