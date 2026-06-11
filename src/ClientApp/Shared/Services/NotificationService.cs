using MudBlazor;
using DatasetStudio.Core.Utilities;
using DatasetStudio.Core.Utilities.Logging;

namespace DatasetStudio.ClientApp.Shared.Services;

/// <summary>Provides toast notification functionality using MudBlazor Snackbar.</summary>
public class NotificationService(ISnackbar snackbar)
{
    public ISnackbar Snackbar { get; } = snackbar;

    /// <summary>Displays a success notification with green styling.</summary>
    /// <param name="message">Success message to display.</param>
    /// <param name="duration">Duration in seconds, default 3.</param>
    public void ShowSuccess(string message, int duration = 3)
    {
        Snackbar.Configuration.PositionClass = Defaults.Classes.Position.BottomRight;
        Snackbar.Add(message, Severity.Success, config =>
        {
            config.VisibleStateDuration = duration * 1000;
        });
        Logs.Info($"Success notification: {message}");
    }

    /// <summary>Displays an error notification with red styling.</summary>
    /// <param name="message">Error message to display.</param>
    /// <param name="duration">Duration in seconds, default 5.</param>
    public void ShowError(string message, int duration = 5)
    {
        Snackbar.Configuration.PositionClass = Defaults.Classes.Position.BottomRight;
        Snackbar.Add(message, Severity.Error, config =>
        {
            config.VisibleStateDuration = duration * 1000;
        });
        Logs.Error($"Error notification: {message}");
    }

    /// <summary>Displays a warning notification with orange styling.</summary>
    /// <param name="message">Warning message to display.</param>
    /// <param name="duration">Duration in seconds, default 4.</param>
    public void ShowWarning(string message, int duration = 4)
    {
        Snackbar.Configuration.PositionClass = Defaults.Classes.Position.BottomRight;
        Snackbar.Add(message, Severity.Warning, config =>
        {
            config.VisibleStateDuration = duration * 1000;
        });
        Logs.Info($"Warning notification: {message}");
    }

    /// <summary>Displays an informational notification with blue styling.</summary>
    /// <param name="message">Information message to display.</param>
    /// <param name="duration">Duration in seconds, default 3.</param>
    public void ShowInfo(string message, int duration = 3)
    {
        Snackbar.Configuration.PositionClass = Defaults.Classes.Position.BottomRight;
        Snackbar.Add(message, Severity.Info, config =>
        {
            config.VisibleStateDuration = duration * 1000;
        });
        Logs.Info($"Info notification: {message}");
    }

    /// <summary>Displays a notification for long-running operations with custom action.</summary>
    /// <param name="message">Message to display.</param>
    /// <param name="actionText">Text for action button.</param>
    /// <param name="action">Action to perform when button clicked.</param>
    public void ShowWithAction(string message, string actionText, Action action)
    {
        Snackbar.Configuration.PositionClass = Defaults.Classes.Position.BottomRight;
        Snackbar.Add(message, Severity.Normal, config =>
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

    /// <summary>Clears all currently visible notifications.</summary>
    public void ClearAll()
    {
        Snackbar.Clear();
        Logs.Info("All notifications cleared");
    }
    
    // TODO: Add notification history/log
    // TODO: Add notification preferences (position, duration defaults)
    // TODO: Add support for custom notification templates
}
