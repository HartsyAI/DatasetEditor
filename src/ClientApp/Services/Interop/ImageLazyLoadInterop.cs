using Microsoft.JSInterop;
using DatasetStudio.Core.Utilities;
using DatasetStudio.Core.Utilities.Logging;

namespace DatasetStudio.ClientApp.Services.Interop;

/// <summary>
/// Wrapper around IntersectionObserver-based lazy loading helper.
/// TODO: Implement corresponding JS in wwwroot/js/interop.js.
/// </summary>
public sealed class ImageLazyLoadInterop(IJSRuntime jsRuntime)
{
    private readonly IJSRuntime _jsRuntime = jsRuntime;

    /// <summary>
    /// Registers a DOM element for lazy loading.
    /// TODO: Accept optional threshold/rootMargin parameters once design requires tuning.
    /// </summary>
    public async ValueTask RegisterAsync(string elementId)
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("imageLazyLoad.register", elementId);
        }
        catch (Exception ex)
        {
            Logs.Error($"Failed to register image '{elementId}' for lazy loading", ex);
            throw;
        }
    }

    /// <summary>
    /// Unregisters the element to clean up observers when components dispose.
    /// </summary>
    public async ValueTask UnregisterAsync(string elementId)
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("imageLazyLoad.unregister", elementId);
        }
        catch (Exception ex)
        {
            Logs.Error($"Failed to unregister image '{elementId}' from lazy loading", ex);
        }
    }

    /// <summary>
    /// Disconnects the IntersectionObserver instance.
    /// Useful when shutting down large image grids.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("imageLazyLoad.dispose");
        }
        catch (Exception ex)
        {
            Logs.Error("Failed to dispose image lazy load observer", ex);
        }
    }
}
