using DatasetStudio.ClientApp.Services.ApiClients;
using DatasetStudio.DTO.Datasets;
using Microsoft.AspNetCore.Components;

namespace DatasetStudio.ClientApp.Features.Datasets.Components;

/// <summary>
/// Create a dataset that streams from an S3 or S3-compatible bucket through a server-side storage
/// profile. Items load on demand, so the form opens the dataset as soon as the import is accepted.
/// </summary>
public partial class S3SourceForm
{
    [Inject] private DatasetApiClient Api { get; set; } = default!;

    private string _profile = string.Empty;
    private string _bucket = string.Empty;
    private string? _prefix;
    private string? _name;

    private Task ConnectAsync()
    {
        string profile = _profile.Trim();
        string bucket = _bucket.Trim();
        string? prefix = string.IsNullOrWhiteSpace(_prefix) ? null : _prefix.Trim();

        return RunAsync("S3 import", async cancellationToken =>
        {
            string name = string.IsNullOrWhiteSpace(_name) ? bucket : _name.Trim();
            string description = prefix is null ? $"S3 bucket: {bucket}" : $"S3 bucket: {bucket}/{prefix}";

            DatasetDetailDto dataset = await Workflow.CreateAsync(name, description, IsPublic, cancellationToken);
            await SetStatusAsync("Connecting to the bucket");
            bool accepted = await Api.ImportFromS3Async(dataset.Id, new ImportS3DatasetRequest(profile, bucket, prefix), cancellationToken);
            if (!accepted)
            {
                await Workflow.DeleteAsync(dataset.Id, CancellationToken.None);
                throw new InvalidOperationException("The server didn't accept the bucket. Check the profile and bucket names.");
            }
            return dataset.Id;
        });
    }
}
