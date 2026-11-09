using MudBlazor;

namespace DatasetStudio.ClientApp.Shared.Components;

/// <summary>Opens <see cref="ConfirmDialog"/> and returns the user's answer.</summary>
public static class DialogServiceExtensions
{
    /// <summary>
    /// Shows a confirmation and returns <c>true</c> only when the user confirms; closing,
    /// cancelling or pressing Escape returns <c>false</c>.
    /// </summary>
    /// <param name="dialogService">MudBlazor's dialog service.</param>
    /// <param name="title">Dialog title.</param>
    /// <param name="message">The question, naming what will happen.</param>
    /// <param name="confirmLabel">Confirm button label (a verb).</param>
    /// <param name="destructive">Style the confirm button as destructive.</param>
    /// <param name="secondaryMessage">Optional consequence line.</param>
    public static async Task<bool> ConfirmAsync(
        this IDialogService dialogService,
        string title,
        string message,
        string confirmLabel,
        bool destructive = false,
        string? secondaryMessage = null)
    {
        DialogParameters<ConfirmDialog> parameters = new()
        {
            { d => d.Title, title },
            { d => d.Message, message },
            { d => d.ConfirmLabel, confirmLabel },
            { d => d.Destructive, destructive },
            { d => d.SecondaryMessage, secondaryMessage }
        };
        DialogOptions options = new()
        {
            MaxWidth = MaxWidth.Small,
            FullWidth = true,
            NoHeader = true,
            CloseOnEscapeKey = true
        };

        IDialogReference reference = await dialogService.ShowAsync<ConfirmDialog>(title, parameters, options);
        DialogResult? result = await reference.Result;
        return result is { Canceled: false };
    }
}
