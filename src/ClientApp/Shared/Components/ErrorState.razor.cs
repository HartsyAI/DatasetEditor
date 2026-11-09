using Microsoft.AspNetCore.Components;

namespace DatasetStudio.ClientApp.Shared.Components;

/// <summary>
/// Inline error for a data view whose request failed (DESIGN.md §5.1), with a retry action.
/// The failure is reported here only, never also as a snackbar (audit U-10: the viewer used to
/// show both, and the library showed "No datasets yet" when its request had failed).
/// </summary>
public partial class ErrorState
{
    /// <summary>Short headline.</summary>
    [Parameter] public string Title { get; set; } = "Something went wrong";

    /// <summary>What failed, in plain language.</summary>
    [Parameter, EditorRequired] public string Message { get; set; } = string.Empty;

    /// <summary>Retries the failed request. The button is shown only when this is set.</summary>
    [Parameter] public EventCallback OnRetry { get; set; }

    /// <summary>Retry button label.</summary>
    [Parameter] public string RetryLabel { get; set; } = "Retry";

    /// <summary>Extra CSS classes (spacing) for the alert.</summary>
    [Parameter] public string? Class { get; set; }

    private bool _retrying;

    private async Task RetryAsync()
    {
        _retrying = true;
        try
        {
            await OnRetry.InvokeAsync();
        }
        finally
        {
            _retrying = false;
        }
    }
}
