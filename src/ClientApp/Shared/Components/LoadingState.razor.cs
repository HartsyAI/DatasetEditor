using Microsoft.AspNetCore.Components;

namespace DatasetStudio.ClientApp.Shared.Components;

/// <summary>Which placeholder <see cref="LoadingState"/> renders.</summary>
public enum LoadingVariant
{
    /// <summary>Centred spinner; only for the viewer's first paint (DESIGN.md §3).</summary>
    Spinner,

    /// <summary>Skeleton cards in the dataset-grid layout.</summary>
    CardGrid,

    /// <summary>Skeleton rows for list-shaped content.</summary>
    Rows
}

/// <summary>
/// First-load placeholder for a data view (DESIGN.md §5.1). Was <c>LoadingIndicator</c>, which no
/// page used; pages hand-rolled spinners with fixed <c>80vh</c> heights instead. A skeleton that
/// resembles the content is preferred over a spinner.
/// </summary>
public partial class LoadingState
{
    /// <summary>Placeholder shape.</summary>
    [Parameter] public LoadingVariant Variant { get; set; } = LoadingVariant.CardGrid;

    /// <summary>Number of skeleton cards or rows.</summary>
    [Parameter] public int Count { get; set; } = 6;

    /// <summary>Text under the spinner, and the accessible name of skeletons.</summary>
    [Parameter] public string Message { get; set; } = "Loading";
}
