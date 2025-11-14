using Microsoft.AspNetCore.Components;
using HartsysDatasetEditor.Core.Utilities;

namespace HartsysDatasetEditor.Client.Services;

/// <summary>Provides navigation helpers and routing utilities for the application.</summary>
public class NavigationService(NavigationManager navigationManager)
{
    public NavigationManager NavigationManager { get; } = navigationManager;

    /// <summary>Navigates to the home/dashboard page.</summary>
    public void NavigateToHome()
    {
        NavigationManager.NavigateTo("/");
        Logs.Info("Navigated to home");
    }

    /// <summary>Navigates to the dataset viewer page with optional dataset ID.</summary>
    /// <param name="datasetId">Optional dataset identifier to load.</param>
    public void NavigateToDataset(string? datasetId = null)
    {
        string url = string.IsNullOrEmpty(datasetId) 
            ? "/dataset-viewer" 
            : $"/dataset-viewer?id={datasetId}";
        NavigationManager.NavigateTo(url);
        Logs.Info($"Navigated to dataset viewer: {datasetId ?? "no dataset specified"}");
    }

    /// <summary>Navigates to the settings page with optional section.</summary>
    /// <param name="section">Optional settings section to open (e.g., "appearance", "display").</param>
    public void NavigateToSettings(string? section = null)
    {
        string url = string.IsNullOrEmpty(section) 
            ? "/settings" 
            : $"/settings?section={section}";
        NavigationManager.NavigateTo(url);
        Logs.Info($"Navigated to settings: {section ?? "general"}");
    }

    /// <summary>Navigates back to the previous page in history.</summary>
    public void NavigateBack()
    {
        // Note: Blazor doesn't have built-in back navigation
        // This would require JavaScript interop to call window.history.back()
        // For now, navigate to home as fallback
        NavigateToHome();
        Logs.Info("Navigate back requested (navigated to home as fallback)");
    }

    /// <summary>Navigates to a specific URL path.</summary>
    /// <param name="url">URL path to navigate to.</param>
    /// <param name="forceLoad">Whether to force a full page reload.</param>
    public void NavigateTo(string url, bool forceLoad = false)
    {
        NavigationManager.NavigateTo(url, forceLoad);
        Logs.Info($"Navigated to: {url} (forceLoad: {forceLoad})");
    }

    /// <summary>Gets the current URI of the application.</summary>
    /// <returns>Current absolute URI.</returns>
    public string GetCurrentUri()
    {
        return NavigationManager.Uri;
    }

    /// <summary>Gets the base URI of the application.</summary>
    /// <returns>Base URI.</returns>
    public string GetBaseUri()
    {
        return NavigationManager.BaseUri;
    }

    /// <summary>Builds a URI with query parameters.</summary>
    /// <param name="basePath">Base path without query string.</param>
    /// <param name="parameters">Dictionary of query parameters.</param>
    /// <returns>Complete URI with query string.</returns>
    public string BuildUriWithParameters(string basePath, Dictionary<string, string> parameters)
    {
        if (parameters == null || parameters.Count == 0)
        {
            return basePath;
        }

        string queryString = string.Join("&", parameters.Select(kvp => 
            $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
        
        return $"{basePath}?{queryString}";
    }

    /// <summary>Extracts query parameters from the current URI.</summary>
    /// <returns>Dictionary of query parameters.</returns>
    public Dictionary<string, string> GetQueryParameters()
    {
        Uri uri = new Uri(NavigationManager.Uri);
        string query = uri.Query;
        
        if (string.IsNullOrEmpty(query))
        {
            return new Dictionary<string, string>();
        }

        return query.TrimStart('?')
            .Split('&')
            .Select(param => param.Split('='))
            .Where(parts => parts.Length == 2)
            .ToDictionary(
                parts => Uri.UnescapeDataString(parts[0]), 
                parts => Uri.UnescapeDataString(parts[1]));
    }

    /// <summary>Gets a specific query parameter value.</summary>
    /// <param name="parameterName">Name of the query parameter.</param>
    /// <returns>Parameter value or null if not found.</returns>
    public string? GetQueryParameter(string parameterName)
    {
        Dictionary<string, string> parameters = GetQueryParameters();
        return parameters.TryGetValue(parameterName, out string? value) ? value : null;
    }
    
    // TODO: Add browser history manipulation (back/forward)
    // TODO: Add navigation guards/confirmation dialogs
    // TODO: Add breadcrumb trail tracking
}
