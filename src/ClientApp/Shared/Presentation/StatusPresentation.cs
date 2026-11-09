using DatasetStudio.DTO.Datasets;
using MudBlazor;

namespace DatasetStudio.ClientApp.Shared.Presentation;

/// <summary>
/// How an <see cref="IngestionStatusDto"/> looks everywhere in the client (DESIGN.md §5.3). The
/// library and the viewer each used to map statuses on their own and disagreed: Pending was
/// Warning in both, and the viewer's banner and the library's chip used different labels.
/// </summary>
/// <param name="Color">Chip colour.</param>
/// <param name="Severity">Alert severity for the same status.</param>
/// <param name="Icon">Material icon (filled).</param>
/// <param name="Label">User-facing label; "Ready" rather than the API's "Completed" (§7).</param>
public sealed record StatusPresentation(Color Color, Severity Severity, string Icon, string Label)
{
    /// <summary>Returns the presentation for a status; unknown values render as neutral.</summary>
    public static StatusPresentation For(IngestionStatusDto status)
    {
        return status switch
        {
            IngestionStatusDto.Pending => new(Color.Default, Severity.Normal, Icons.Material.Filled.Schedule, "Pending"),
            IngestionStatusDto.Processing => new(Color.Info, Severity.Info, Icons.Material.Filled.Sync, "Processing"),
            IngestionStatusDto.Completed => new(Color.Success, Severity.Success, Icons.Material.Filled.CheckCircle, "Ready"),
            IngestionStatusDto.Failed => new(Color.Error, Severity.Error, Icons.Material.Filled.Error, "Failed"),
            _ => new(Color.Default, Severity.Normal, Icons.Material.Filled.HelpOutline, "Unknown")
        };
    }
}
