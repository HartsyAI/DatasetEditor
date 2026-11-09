using Microsoft.AspNetCore.Components;

namespace DatasetStudio.ClientApp.Features.Datasets.Components;

/// <summary>
/// One choice of where a new dataset comes from (DESIGN.md §4.1). The whole card is a native
/// button (keyboard and screen-reader behaviour for free; <c>aria-pressed</c> marks the choice)
/// with the selection ring when chosen. It replaces four hand-rolled cards that each carried a
/// Filled Primary "Use …" button, which put four primary actions on one page.
/// </summary>
public partial class SourceOptionCard
{
    /// <summary>Source name.</summary>
    [Parameter, EditorRequired] public string Title { get; set; } = string.Empty;

    /// <summary>One sentence on what the source is for.</summary>
    [Parameter, EditorRequired] public string Description { get; set; } = string.Empty;

    /// <summary>Icon from the fixed vocabulary (DESIGN.md §2.6).</summary>
    [Parameter, EditorRequired] public string Icon { get; set; } = string.Empty;

    /// <summary>Whether this source is the current choice.</summary>
    [Parameter] public bool Selected { get; set; }

    /// <summary>Raised when the card is chosen.</summary>
    [Parameter] public EventCallback OnSelect { get; set; }
}
