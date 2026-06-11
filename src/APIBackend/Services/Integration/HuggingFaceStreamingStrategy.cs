using System;
using System.Threading;
using System.Threading.Tasks;

namespace DatasetStudio.APIBackend.Services.Integration;

internal sealed class HuggingFaceStreamingPlan
{
    public bool IsStreamingSupported { get; init; }

    public string? Config { get; init; }

    public string? Split { get; init; }

    public long? TotalRows { get; init; }

    public string? Source { get; init; }

    public string? FailureReason { get; init; }
}

internal static class HuggingFaceStreamingStrategy
{
    public static async Task<HuggingFaceStreamingPlan> DiscoverStreamingPlanAsync(
        IHuggingFaceDatasetServerClient datasetServerClient,
        string repository,
        string? accessToken,
        CancellationToken cancellationToken = default)
    {
        if (datasetServerClient == null)
        {
            throw new ArgumentNullException(nameof(datasetServerClient));
        }

        if (string.IsNullOrWhiteSpace(repository))
        {
            throw new ArgumentException("Repository is required", nameof(repository));
        }

        // First, try /size to obtain default config/split and total row count.
        HuggingFaceDatasetSizeInfo? sizeInfo = await datasetServerClient.GetDatasetSizeAsync(
            repository,
            config: null,
            split: null,
            accessToken,
            cancellationToken);

        if (sizeInfo != null)
        {
            string? split = sizeInfo.Split;
            if (string.IsNullOrWhiteSpace(split))
            {
                split = "train";
            }

            return new HuggingFaceStreamingPlan
            {
                IsStreamingSupported = true,
                Config = sizeInfo.Config,
                Split = split,
                TotalRows = sizeInfo.NumRows,
                Source = "size"
            };
        }

        // Some datasets (e.g., very large ones) may not yet support /size.
        // Probe /rows with a minimal request to see if streaming is possible at all.
        try
        {
            HuggingFaceRowsPage? probePage = await datasetServerClient.GetRowsAsync(
                repository,
                config: null,
                split: "train",
                offset: 0,
                length: 1,
                accessToken,
                cancellationToken);

            if (probePage != null)
            {
                string split = string.IsNullOrWhiteSpace(probePage.Split) ? "train" : probePage.Split;

                return new HuggingFaceStreamingPlan
                {
                    IsStreamingSupported = true,
                    Config = probePage.Config,
                    Split = split,
                    TotalRows = probePage.NumRowsTotal,
                    Source = "rows-probe"
                };
            }
        }
        catch
        {
            // The datasets-server client already logs failures; treat as unsupported here.
        }

        return new HuggingFaceStreamingPlan
        {
            IsStreamingSupported = false,
            FailureReason = "datasets-server /size and /rows did not return usable streaming info"
        };
    }
}

