using Microsoft.AspNetCore.Components;
using DatasetStudio.Core.Utilities.Logging;

namespace DatasetStudio.ClientApp.Shared.Services;

/// <summary>Provides navigation helpers and routing utilities for the application.</summary>
public class NavigationService(NavigationManager navigationManager)
{
    public NavigationManager NavigationManager { get; } = navigationManager;

    /// <summary>Navigates to the dataset viewer page with optional dataset ID.</summary>
    /// <param name="datasetId">Optional dataset identifier to load.</param>
    public void NavigateToDataset(string? datasetId = null)
    {
        string url = string.IsNullOrEmpty(datasetId) 
            ? "/dataset-viewer" 
            : $"/datasets/{Uri.EscapeDataString(datasetId)}";
        NavigationManager.NavigateTo(url);
        Logs.Info($"Navigated to dataset viewer: {datasetId ?? "no dataset specified"}");
    }

    /// <summary>Navigates to a specific URL path.</summary>
    /// <param name="url">URL path to navigate to.</param>
    /// <param name="forceLoad">Whether to force a full page reload.</param>
    public void NavigateTo(string url, bool forceLoad = false)
    {
        NavigationManager.NavigateTo(url, forceLoad);
        Logs.Info($"Navigated to: {url} (forceLoad: {forceLoad})");
    }

    /// <summary>Gets a specific query parameter value.</summary>
    /// <param name="parameterName">Name of the query parameter.</param>
    /// <returns>Parameter value or null if not found.</returns>
    public string? GetQueryParameter(string parameterName)
    {
        string query = new Uri(NavigationManager.Uri).Query.TrimStart('?');
        foreach (string pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = pair.Split('=', 2);
            if (parts.Length == 2 && Uri.UnescapeDataString(parts[0]) == parameterName)
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }
        return null;
    }
}
