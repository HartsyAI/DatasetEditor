using DatasetStudio.ClientApp.Features.Datasets.Services;
using DatasetStudio.DTO.Datasets;
using Microsoft.AspNetCore.Components.Forms;

namespace DatasetStudio.ClientApp.Features.Datasets.Components;

/// <summary>
/// Create a dataset from one uploaded file (CSV, TSV, JSON, JSONL, Parquet, or a ZIP of them). The old
/// uploader accepted several files and read every non-ZIP file fully into a string in the browser
/// to label it "primary" or "enrichment" (audit K-8), then uploaded only the primary: enrichment
/// files were never sent. Drag and drop now works (the file input covers the drop zone; the old
/// drop handler was a no-op), and the file streams to the API without an in-memory copy (U-14).
/// </summary>
public partial class UploadSourceForm
{
    private IBrowserFile? _file;
    private string _name = string.Empty;
    private bool _isDragging;
    private Guid _inputKey = Guid.NewGuid();

    private void OnFileSelected(InputFileChangeEventArgs args)
    {
        _isDragging = false;
        Error = null;
        IBrowserFile file = args.File;
        if (file.Size > IngestionWorkflowService.MaxUploadBytes)
        {
            Error = $"{file.Name} is {FormatSize(file.Size)}; the limit is 5 GB.";
            ClearFile();
            return;
        }
        string extension = Path.GetExtension(file.Name).ToLowerInvariant();
        if (!IngestionWorkflowService.AcceptedUploadExtensions.Split(',').Contains(extension))
        {
            Error = $"{file.Name} isn't a supported type. Use CSV, TSV, JSON, JSONL, Parquet or ZIP.";
            ClearFile();
            return;
        }

        _file = file;
        if (string.IsNullOrWhiteSpace(_name))
        {
            _name = Path.GetFileNameWithoutExtension(file.Name);
        }
    }

    private void ClearFile()
    {
        _file = null;
        // A new key re-creates the input so choosing the same file again still raises OnChange.
        _inputKey = Guid.NewGuid();
    }

    private Task UploadAsync()
    {
        IBrowserFile? file = _file;
        if (file is null)
        {
            return Task.CompletedTask;
        }

        return RunAsync("Upload", async cancellationToken =>
        {
            DatasetDto dataset = await Workflow.CreateAsync(_name.Trim(), $"Uploaded from {file.Name}", IsPublic, cancellationToken);
            await SetStatusAsync($"Uploading {file.Name} ({FormatSize(file.Size)})");
            await Workflow.UploadAsync(dataset.Id, file, cancellationToken);
            return dataset.Id;
        });
    }

    /// <summary>Human-readable size, for example "12.5 MB".</summary>
    public static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} B" : $"{size:0.#} {units[unit]}";
    }
}
