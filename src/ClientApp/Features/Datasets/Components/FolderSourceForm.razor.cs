using DatasetStudio.ClientApp.Services.ApiClients;
using DatasetStudio.DTO.Datasets;
using Microsoft.AspNetCore.Components;

namespace DatasetStudio.ClientApp.Features.Datasets.Components;

/// <summary>
/// Create a dataset from a folder already on the server (admins only, enforced by the API). The
/// form opens the dataset as soon as the server accepts the import; the viewer's status band
/// follows the scan (the old flow slept 2 s and often said "check the dashboard in a moment").
/// </summary>
public partial class FolderSourceForm
{
    [Inject] private DatasetApiClient Api { get; set; } = default!;

    private string _folderPath = string.Empty;
    private string? _name;

    private Task ImportAsync()
    {
        string folder = _folderPath.Trim();
        return RunAsync("Folder import", async cancellationToken =>
        {
            string name = string.IsNullOrWhiteSpace(_name)
                ? Path.GetFileName(folder.TrimEnd('/', '\\')) is { Length: > 0 } leaf ? leaf : folder
                : _name.Trim();

            DatasetDto dataset = await Workflow.CreateAsync(name, $"Server folder: {folder}", IsPublic, cancellationToken);
            await SetStatusAsync("Starting the folder scan");
            bool accepted = await Api.ImportFromFolderAsync(dataset.Id, new ImportFolderRequest(folder), cancellationToken);
            if (!accepted)
            {
                await Workflow.DeleteAsync(dataset.Id, CancellationToken.None);
                throw new InvalidOperationException("The server didn't accept the folder. Check the path, that it is inside one of the server's allowed import folders (Storage:FolderImportRoots), and that you are an admin.");
            }
            return dataset.Id;
        });
    }
}
