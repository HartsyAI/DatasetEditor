using Microsoft.JSInterop;
using HartsysDatasetEditor.Core.Utilities;

namespace HartsysDatasetEditor.Client.Services.JsInterop;

/// <summary>
/// Provides typed helpers for browser LocalStorage interactions.
/// TODO: Wire up actual JS implementations in wwwroot/js/interop.js.
/// </summary>
public sealed class LocalStorageInterop(IJSRuntime jsRuntime)
{
    private readonly IJSRuntime _jsRuntime = jsRuntime;

    /// <summary>
    /// Saves a value to LocalStorage.
    /// TODO: Consider JSON serialization via System.Text.Json options aligned with DatasetState persistence needs.
    /// </summary>
    public async Task SetItemAsync(string key, string value)
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("localStorageInterop.setItem", key, value);
        }
        catch (Exception ex)
        {
            Logs.Error($"Failed to set LocalStorage key '{key}'", ex);
            throw;
        }
    }

    /// <summary>
    /// Retrieves a value from LocalStorage.
    /// TODO: Callers should handle null return indicating missing key.
    /// </summary>
    public async Task<string?> GetItemAsync(string key)
    {
        try
        {
            return await _jsRuntime.InvokeAsync<string?>("localStorageInterop.getItem", key);
        }
        catch (Exception ex)
        {
            Logs.Error($"Failed to get LocalStorage key '{key}'", ex);
            return null;
        }
    }

    /// <summary>
    /// Removes a key from LocalStorage.
    /// </summary>
    public async Task RemoveItemAsync(string key)
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("localStorageInterop.removeItem", key);
        }
        catch (Exception ex)
        {
            Logs.Error($"Failed to remove LocalStorage key '{key}'", ex);
        }
    }

    /// <summary>
    /// Clears all keys. Use cautiously—likely only during "reset app" flows.
    /// </summary>
    public async Task ClearAsync()
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("localStorageInterop.clear");
        }
        catch (Exception ex)
        {
            Logs.Error("Failed to clear LocalStorage", ex);
        }
    }
}
