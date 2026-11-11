using DatasetStudio.ClientApp.Features.Datasets.Services;
using DatasetStudio.ClientApp.Services.ApiClients;
using DatasetStudio.ClientApp.Shared.Components;
using DatasetStudio.Core.Utilities.Logging;
using DatasetStudio.DTO.Datasets;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace DatasetStudio.ClientApp.Features.Datasets.Components;

/// <summary>
/// Create a dataset from a HuggingFace repository, streamed or imported. Discovery runs first;
/// when there is more than one config/split (streaming) or file (import) the user picks one. If
/// streaming turns out to be unavailable for the repository, the server marks the dataset Failed
/// with a STREAMING_UNAVAILABLE reason; the form waits for that status (instead of a fixed 2 s
/// delay) and offers to import a copy instead.
/// </summary>
public partial class HuggingFaceSourceForm
{
    private const string StreamingUnavailablePrefix = "STREAMING_UNAVAILABLE:";

    [Inject] private DatasetApiClient Api { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;

    private string _repository = string.Empty;
    private bool _isStreaming = true;
    private string? _name;
    private string? _description;
    private string? _revision;
    private string? _accessToken;
    private bool _isDiscovering;
    private HuggingFaceDiscoveryResponse? _discovery;

    private string Repository => _repository.Trim();

    private async Task DiscoverAsync()
    {
        Error = null;
        _isDiscovering = true;
        try
        {
            HuggingFaceDiscoveryResponse? response = await Api.DiscoverHuggingFaceDatasetAsync(
                new HuggingFaceDiscoveryRequest
                {
                    Repository = Repository,
                    Revision = _revision,
                    IsStreaming = _isStreaming,
                    AccessToken = _accessToken
                },
                Lifetime);

            if (response is null || !response.IsAccessible)
            {
                Error = response?.ErrorMessage ?? "That repository couldn't be read. Check the name, or add an access token if it is gated.";
                return;
            }

            bool needsChoice = _isStreaming
                ? response.StreamingOptions?.AvailableOptions.Count > 1
                : response.DownloadOptions?.AvailableFiles.Count > 1;

            if (needsChoice)
            {
                _discovery = response;
            }
            else
            {
                await ImportAsync(config: null, split: null, dataFile: null);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logs.Error("HuggingFace discovery failed", ex);
            Error = IngestionWorkflowService.DescribeFailure(ex);
        }
        finally
        {
            _isDiscovering = false;
        }
    }

    private Task OnOptionsConfirmed((string? Config, string? Split, string? DataFilePath) choice)
    {
        _discovery = null;
        return ImportAsync(choice.Config, choice.Split, choice.DataFilePath);
    }

    private Task ImportAsync(string? config, string? split, string? dataFile)
    {
        return RunAsync("HuggingFace import", cancellationToken => ImportStepsAsync(config, split, dataFile, _isStreaming, cancellationToken));
    }

    private async Task<Guid?> ImportStepsAsync(string? config, string? split, string? dataFile, bool streaming, CancellationToken cancellationToken)
    {
        string name = string.IsNullOrWhiteSpace(_name) ? Repository.Split('/').Last() : _name.Trim();
        string description = string.IsNullOrWhiteSpace(_description) ? $"From HuggingFace: {Repository}" : _description.Trim();

        DatasetDetailDto dataset = await Workflow.CreateAsync(name, description, IsPublic, cancellationToken);

        await SetStatusAsync(streaming ? "Connecting to the repository" : "Starting the import");
        bool accepted = await Api.ImportFromHuggingFaceAsync(
            dataset.Id,
            new ImportHuggingFaceDatasetRequest
            {
                Repository = Repository,
                Revision = _revision,
                Name = name,
                Description = description,
                IsStreaming = streaming,
                AccessToken = _accessToken,
                Config = config,
                Split = split,
                DataFilePath = dataFile
            },
            cancellationToken);
        if (!accepted)
        {
            await Workflow.DeleteAsync(dataset.Id, CancellationToken.None);
            throw new InvalidOperationException("The server didn't accept the import.");
        }

        if (!streaming)
        {
            // A copy is imported in the background; the viewer's status band follows it.
            return dataset.Id;
        }

        // Streaming is set up quickly; wait until the server leaves Pending to learn whether the
        // repository can actually be streamed.
        await SetStatusAsync("Checking that the repository can be streamed");
        DatasetDetailDto? status = await Workflow.WaitForStatusAsync(
            dataset.Id, d => d.Status != IngestionStatusDto.Pending, TimeSpan.FromSeconds(30), cancellationToken);

        if (status is { Status: IngestionStatusDto.Failed } && status.ErrorMessage?.StartsWith(StreamingUnavailablePrefix, StringComparison.Ordinal) == true)
        {
            string reason = status.ErrorMessage.Substring(StreamingUnavailablePrefix.Length).Trim();
            await Workflow.DeleteAsync(dataset.Id, CancellationToken.None);

            bool importCopy = await DialogService.ConfirmAsync(
                title: "Streaming isn't available",
                message: $"This repository can't be streamed: {reason}",
                confirmLabel: "Import a copy",
                secondaryMessage: "Importing downloads the data to this server, which can take a while and use a lot of disk space.");
            if (!importCopy)
            {
                Notifications.ShowWarning("Import cancelled.");
                return null;
            }

            return await ImportStepsAsync(config, split, dataFile, streaming: false, cancellationToken);
        }

        return dataset.Id;
    }
}
