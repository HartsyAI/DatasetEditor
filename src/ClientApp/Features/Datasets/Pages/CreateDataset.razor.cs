using MudBlazor;

namespace DatasetStudio.ClientApp.Features.Datasets.Pages;

/// <summary>Where a new dataset's items come from.</summary>
public enum CreateSource
{
    /// <summary>Files or a ZIP uploaded from the browser.</summary>
    Upload,

    /// <summary>A HuggingFace dataset repository, streamed or imported.</summary>
    HuggingFace,

    /// <summary>A folder of images already on the server.</summary>
    Folder,

    /// <summary>An S3 or S3-compatible bucket, streamed.</summary>
    S3
}

/// <summary>
/// Create dataset: choose a source (step 1), then configure and import it (step 2). Step 2 is
/// disabled until a source is chosen. The page has one primary action, Continue; the source cards
/// are selections, not buttons. Each source has its own form
/// component (<c>UploadSourceForm</c>, <c>HuggingFaceSourceForm</c>, <c>FolderSourceForm</c>,
/// <c>S3SourceForm</c>), which opens the new dataset when the server accepts it.
/// </summary>
public partial class CreateDataset
{
    /// <summary>One source card's content.</summary>
    private sealed record SourceOption(CreateSource Source, string Title, string Description, string Icon);

    private static readonly SourceOption[] _options =
    [
        new(CreateSource.Upload, "Upload files", "CSV, TSV, JSON or JSONL, or a ZIP of them, from your computer.", Icons.Material.Filled.CloudUpload),
        new(CreateSource.HuggingFace, "HuggingFace", "Stream or import a dataset repository from HuggingFace.", Icons.Material.Filled.Cloud),
        new(CreateSource.Folder, "Server folder", "Index a folder of images that is already on the server's disk.", Icons.Material.Filled.Folder),
        new(CreateSource.S3, "S3 bucket", "Stream images from an S3 or S3-compatible bucket (MinIO, R2, Backblaze).", Icons.Material.Filled.Storage)
    ];

    private int _activeStep;
    private CreateSource? _selectedSource;

    private string SelectedTitle => _options.First(o => o.Source == _selectedSource).Title;

    private void SelectSource(CreateSource source)
    {
        _selectedSource = source;
    }

    private void Continue()
    {
        if (_selectedSource is not null)
        {
            _activeStep = 1;
        }
    }

    private void Back()
    {
        _activeStep = 0;
    }
}
