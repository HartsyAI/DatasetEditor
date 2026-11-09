using Microsoft.AspNetCore.Components;

namespace DatasetStudio.ClientApp.Shared.Components;

/// <summary>
/// One labelled number (DESIGN.md §4.1), such as the dashboard's "Current session" counts.
/// Caption label above a value, so every stat reads the same wherever it appears.
/// </summary>
public partial class StatCard
{
    /// <summary>What the number is.</summary>
    [Parameter, EditorRequired] public string Label { get; set; } = string.Empty;

    /// <summary>The formatted value.</summary>
    [Parameter, EditorRequired] public string Value { get; set; } = string.Empty;
}
