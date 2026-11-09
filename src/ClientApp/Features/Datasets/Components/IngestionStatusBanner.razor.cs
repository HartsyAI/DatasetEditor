using DatasetStudio.DTO.Datasets;
using Microsoft.AspNetCore.Components;

namespace DatasetStudio.ClientApp.Features.Datasets.Components;

/// <summary>
/// The viewer's status band (DESIGN.md §3.1): ingestion status, when it last changed, the item
/// count, a refresh button, progress while importing, and the error when it failed (which the
/// inline version in the page never showed). Extracted from <c>DatasetViewer</c>.
/// </summary>
public partial class IngestionStatusBanner
{
    /// <summary>The dataset's current detail record.</summary>
    [Parameter, EditorRequired] public DatasetDetailDto Detail { get; set; } = default!;

    /// <summary>Whether a manual refresh is in flight (disables the button).</summary>
    [Parameter] public bool IsRefreshing { get; set; }

    /// <summary>Refreshes the status now.</summary>
    [Parameter] public EventCallback OnRefresh { get; set; }

    private bool IsRunning => Detail.Status is IngestionStatusDto.Pending or IngestionStatusDto.Processing;
}
