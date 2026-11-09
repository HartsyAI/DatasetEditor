using Microsoft.AspNetCore.Components;

namespace DatasetStudio.ClientApp.Shared.Components;

/// <summary>
/// Page title block (DESIGN.md §3): the page's single <c>h4</c>, rendered as an <c>h1</c> so
/// <c>FocusOnNavigate Selector="h1"</c> moves focus on navigation, an optional subtitle, and the
/// page's primary action on the right. Replaces a hand-rolled title on every page.
/// </summary>
public partial class PageHeader
{
    /// <summary>The page title.</summary>
    [Parameter, EditorRequired] public string Title { get; set; } = string.Empty;

    /// <summary>Optional one-line description under the title.</summary>
    [Parameter] public string? Subtitle { get; set; }

    /// <summary>Optional rich subtitle (for example with a link); wins over <see cref="Subtitle"/>.</summary>
    [Parameter] public RenderFragment? SubtitleContent { get; set; }

    /// <summary>Optional overline above the title.</summary>
    [Parameter] public string? Eyebrow { get; set; }

    /// <summary>The page's primary action (one <c>Filled Primary</c> button), right-aligned.</summary>
    [Parameter] public RenderFragment? Actions { get; set; }
}
