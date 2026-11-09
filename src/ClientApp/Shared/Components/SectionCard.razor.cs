using Microsoft.AspNetCore.Components;

namespace DatasetStudio.ClientApp.Shared.Components;

/// <summary>
/// A titled panel (DESIGN.md §4.1): <c>subtitle1</c> title, optional description and icon, body,
/// and actions pinned to the bottom. The dashboard and settings panels each hand-rolled this as a
/// <c>MudPaper</c> or <c>MudCard</c> with their own spacing.
/// </summary>
public partial class SectionCard
{
    /// <summary>Panel title.</summary>
    [Parameter, EditorRequired] public string Title { get; set; } = string.Empty;

    /// <summary>Optional one-line description under the title.</summary>
    [Parameter] public string? Subtitle { get; set; }

    /// <summary>Optional icon at the top-right, from the fixed vocabulary (DESIGN.md §2.6).</summary>
    [Parameter] public string? Icon { get; set; }

    /// <summary>Panel body.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Buttons at the bottom of the panel.</summary>
    [Parameter] public RenderFragment? Actions { get; set; }

    /// <summary>Elevation: 1 for panels, 2 for dashboard feature cards (DESIGN.md §2.5).</summary>
    [Parameter] public int Elevation { get; set; } = 1;

    /// <summary>Extra CSS classes.</summary>
    [Parameter] public string? Class { get; set; }
}
