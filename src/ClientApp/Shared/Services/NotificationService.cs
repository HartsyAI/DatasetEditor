using DatasetStudio.Core.Utilities.Logging;
using MudBlazor;

namespace DatasetStudio.ClientApp.Shared.Services;

/// <summary>
/// The only way the client shows a snackbar (DESIGN.md §4.7). Severity follows MudBlazor: success
/// for an action the user just completed, error for one that failed, info for background progress.
/// Snackbars are not used for validation or for failures already shown inline (audit U-10).
/// The underlying <see cref="ISnackbar"/> is private so pages can't bypass this and drift on
/// position and duration again.
/// </summary>
public class NotificationService
{
    private readonly ISnackbar _snackbar;

    /// <summary>Creates the service and pins snackbars to the bottom-right corner.</summary>
    public NotificationService(ISnackbar snackbar)
    {
        _snackbar = snackbar;
        _snackbar.Configuration.PositionClass = Defaults.Classes.Position.BottomRight;
    }

    /// <summary>Confirms an action the user just completed.</summary>
    /// <param name="message">Message to display.</param>
    /// <param name="duration">Seconds visible, default 3.</param>
    public void ShowSuccess(string message, int duration = 3)
    {
        Add(message, Severity.Success, duration);
        Logs.Info($"Success notification: {message}");
    }

    /// <summary>Reports that an action the user just took failed.</summary>
    /// <param name="message">Message to display.</param>
    /// <param name="duration">Seconds visible, default 5.</param>
    public void ShowError(string message, int duration = 5)
    {
        Add(message, Severity.Error, duration);
        Logs.Error($"Error notification: {message}");
    }

    /// <summary>Warns about something that needs attention but didn't fail.</summary>
    /// <param name="message">Message to display.</param>
    /// <param name="duration">Seconds visible, default 4.</param>
    public void ShowWarning(string message, int duration = 4)
    {
        Add(message, Severity.Warning, duration);
        Logs.Info($"Warning notification: {message}");
    }

    /// <summary>Reports background progress ("Import started").</summary>
    /// <param name="message">Message to display.</param>
    /// <param name="duration">Seconds visible, default 3.</param>
    public void ShowInfo(string message, int duration = 3)
    {
        Add(message, Severity.Info, duration);
        Logs.Info($"Info notification: {message}");
    }

    /// <summary>Shows a message with one action button.</summary>
    /// <param name="message">Message to display.</param>
    /// <param name="actionText">Action button label.</param>
    /// <param name="action">Runs when the button is clicked.</param>
    public void ShowWithAction(string message, string actionText, Action action)
    {
        _snackbar.Add(message, Severity.Normal, config =>
        {
            config.Action = actionText;
            config.ActionColor = Color.Primary;
            config.Onclick = _ =>
            {
                action();
                return Task.CompletedTask;
            };
        });
    }

    /// <summary>Removes every visible snackbar.</summary>
    public void ClearAll()
    {
        _snackbar.Clear();
    }

    private void Add(string message, Severity severity, int durationSeconds)
    {
        _snackbar.Add(message, severity, config =>
        {
            config.VisibleStateDuration = durationSeconds * 1000;
        });
    }
}
