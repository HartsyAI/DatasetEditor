using Microsoft.AspNetCore.Components;

namespace DatasetStudio.ClientApp.Shared.Components;

/// <summary>
/// The search/filter strip under a page header and the viewer's action bar (DESIGN.md §4.1):
/// an elevation-1 paper with a wrapping row, start content on the left and end content on the right.
/// </summary>
public partial class Toolbar
{
    /// <summary>Left-aligned content (search, filters, title).</summary>
    [Parameter] public RenderFragment? Start { get; set; }

    /// <summary>Right-aligned content (view toggles, secondary actions).</summary>
    [Parameter] public RenderFragment? End { get; set; }

    /// <summary>Left-aligned content when <see cref="Start"/> is not used.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Extra CSS classes (spacing) for the paper.</summary>
    [Parameter] public string Class { get; set; } = "mb-4";
}
