using DatasetStudio.ClientApp.Features.Datasets.Services;
using DatasetStudio.ClientApp.Shared.Services;
using DatasetStudio.Core.Utilities.Logging;
using Microsoft.AspNetCore.Components;

namespace DatasetStudio.ClientApp.Features.Datasets.Components;

/// <summary>
/// What every create-dataset form shares: the public switch, the busy/progress state, one inline
/// error, and "open the new dataset when the server has accepted it". <c>DatasetUploader</c>
/// repeated this per source, with five copies of its form-reset block (audit U-26).
/// </summary>
public abstract class CreateSourceFormBase : ComponentBase, IDisposable
{
    [Inject] protected IngestionWorkflowService Workflow { get; set; } = default!;
    [Inject] protected NavigationService Navigation { get; set; } = default!;
    [Inject] protected NotificationService Notifications { get; set; } = default!;

    private readonly CancellationTokenSource _disposed = new();

    /// <summary>Whether the new dataset is visible to everyone (off by default).</summary>
    protected bool IsPublic { get; set; }

    /// <summary>Whether a create/import is running.</summary>
    protected bool IsBusy { get; private set; }

    /// <summary>The step currently running, shown in the progress panel.</summary>
    protected string Status { get; private set; } = string.Empty;

    /// <summary>The last failure, shown once inline (not also as a snackbar).</summary>
    protected string? Error { get; set; }

    /// <summary>Cancelled when the form goes away, so an in-flight wait doesn't outlive it.</summary>
    protected CancellationToken Lifetime => _disposed.Token;

    /// <summary>Updates the progress text.</summary>
    protected Task SetStatusAsync(string status)
    {
        Status = status;
        return InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Runs the form's steps. When they return a dataset id, says the import started and opens that
    /// dataset in the viewer, whose status band follows ingestion from there. Returning null means
    /// the steps handled the outcome themselves (for example the user declined a fallback).
    /// </summary>
    /// <param name="what">Log label, for example "HuggingFace import".</param>
    /// <param name="steps">The work; returns the dataset to open, or null.</param>
    protected async Task RunAsync(string what, Func<CancellationToken, Task<Guid?>> steps)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        Error = null;
        Status = "Creating the dataset";
        await InvokeAsync(StateHasChanged);

        try
        {
            Guid? datasetId = await steps(Lifetime);
            if (datasetId is Guid id)
            {
                Notifications.ShowInfo("Import started. Its progress shows at the top of the viewer.");
                Navigation.NavigateToDataset(id.ToString());
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !Lifetime.IsCancellationRequested)
        {
            Logs.Error($"{what} failed", ex);
            Error = IngestionWorkflowService.DescribeFailure(ex);
        }
        catch (OperationCanceledException)
        {
            // The form was closed mid-import; nothing left to show.
        }
        finally
        {
            IsBusy = false;
            Status = string.Empty;
            if (!Lifetime.IsCancellationRequested)
            {
                await InvokeAsync(StateHasChanged);
            }
        }
    }

    /// <summary>Stops in-flight waits when the form is removed.</summary>
    public void Dispose()
    {
        // Cancel only: RunAsync's finally may still read Lifetime after disposal, and a disposed
        // source would throw there. The source holds no unmanaged resources without a timer.
        _disposed.Cancel();
        GC.SuppressFinalize(this);
    }
}
