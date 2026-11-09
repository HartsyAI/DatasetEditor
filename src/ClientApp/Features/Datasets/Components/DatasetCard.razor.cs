using DatasetStudio.DTO.Datasets;
using Microsoft.AspNetCore.Components;

namespace DatasetStudio.ClientApp.Features.Datasets.Components;

/// <summary>
/// One dataset in the datasets grid (DESIGN.md §4.1): name, size, status, source, description,
/// Open/Delete, and "Updated … ago" with the absolute time in a tooltip. Extracted from the page so
/// the card exists once; the page decides what Open and Delete do.
/// </summary>
public partial class DatasetCard
{
    /// <summary>The dataset to show.</summary>
    [Parameter, EditorRequired] public DatasetSummaryDto Dataset { get; set; } = default!;

    /// <summary>Whether the caller owns the dataset and may delete it.</summary>
    [Parameter] public bool CanDelete { get; set; }

    /// <summary>Opens the dataset in the viewer.</summary>
    [Parameter] public EventCallback OnOpen { get; set; }

    /// <summary>Starts deleting the dataset; the page confirms first.</summary>
    [Parameter] public EventCallback OnDelete { get; set; }
}
