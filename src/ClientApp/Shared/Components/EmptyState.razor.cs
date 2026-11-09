using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace DatasetStudio.ClientApp.Shared.Components;

/// <summary>
/// "Nothing here" for a data view that loaded successfully with zero results (DESIGN.md §5.1).
/// The copy should say why: no datasets yet, no matches for the filters, or no items in a dataset.
/// It is never used for a failed request; that is <see cref="ErrorState"/>.
/// </summary>
public partial class EmptyState
{
    /// <summary>Short headline.</summary>
    [Parameter] public string Headline { get; set; } = "Nothing here yet";

    /// <summary>Optional sentence explaining why it's empty or what to do.</summary>
    [Parameter] public string? Description { get; set; }

    /// <summary>Icon above the headline (filled set).</summary>
    [Parameter] public string Icon { get; set; } = Icons.Material.Filled.FolderOpen;

    /// <summary>Optional call to action, styled the same as the page's primary action.</summary>
    [Parameter] public RenderFragment? ActionContent { get; set; }
}
