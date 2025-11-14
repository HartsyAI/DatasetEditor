using HartsysDatasetEditor.Core.Utilities;

namespace HartsysDatasetEditor.Client.Services.StateManagement;

/// <summary>Root application state managing global app-level data and initialization status.</summary>
public class AppState
{
    /// <summary>Indicates whether the application has completed initialization.</summary>
    public bool IsInitialized { get; private set; }
    
    /// <summary>Current authenticated user identifier, null if not authenticated.</summary>
    public string? CurrentUser { get; private set; }
    
    /// <summary>Application version for display purposes.</summary>
    public string Version { get; private set; } = "1.0.0-MVP";
    
    /// <summary>Timestamp when the application was last initialized.</summary>
    public DateTime? InitializedAt { get; private set; }
    
    /// <summary>Event fired when any state property changes.</summary>
    public event Action? OnChange;
    
    /// <summary>Marks the application as initialized and records the initialization timestamp.</summary>
    public void MarkInitialized()
    {
        IsInitialized = true;
        InitializedAt = DateTime.UtcNow;
        NotifyStateChanged();
        Logs.Info("Application state initialized");
    }
    
    /// <summary>Sets the current user identifier.</summary>
    /// <param name="userId">User identifier to set.</param>
    public void SetCurrentUser(string? userId)
    {
        CurrentUser = userId;
        NotifyStateChanged();
        Logs.Info($"Current user set: {userId ?? "anonymous"}");
    }
    
    /// <summary>Resets the application state to its initial values.</summary>
    public void Reset()
    {
        IsInitialized = false;
        CurrentUser = null;
        InitializedAt = null;
        NotifyStateChanged();
        Logs.Info("Application state reset");
    }
    
    /// <summary>Notifies all subscribers that the state has changed.</summary>
    protected void NotifyStateChanged()
    {
        OnChange?.Invoke();
    }
}
