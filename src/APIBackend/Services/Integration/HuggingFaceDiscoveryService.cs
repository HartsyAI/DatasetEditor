using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DatasetStudio.APIBackend.Models;
using DatasetStudio.DTO.Datasets;
using DatasetStudio.Core.Utilities;
using DatasetStudio.Core.Utilities.Logging;

namespace DatasetStudio.APIBackend.Services.Integration;

/// <summary>
/// Service for discovering HuggingFace dataset capabilities (streaming, download options, etc.)
/// </summary>
public interface IHuggingFaceDiscoveryService
{
    Task<HuggingFaceDiscoveryResponse> DiscoverDatasetAsync(
        HuggingFaceDiscoveryRequest request,
        CancellationToken cancellationToken = default);
}

internal sealed class HuggingFaceDiscoveryService : IHuggingFaceDiscoveryService
{
    private readonly IHuggingFaceClient _huggingFaceClient;
    private readonly IHuggingFaceDatasetServerClient _datasetServerClient;

    public HuggingFaceDiscoveryService(
        IHuggingFaceClient huggingFaceClient,
        IHuggingFaceDatasetServerClient datasetServerClient)
    {
        _huggingFaceClient = huggingFaceClient ?? throw new ArgumentNullException(nameof(huggingFaceClient));
        _datasetServerClient = datasetServerClient ?? throw new ArgumentNullException(nameof(datasetServerClient));
    }

    public async Task<HuggingFaceDiscoveryResponse> DiscoverDatasetAsync(
        HuggingFaceDiscoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        Logs.Info($"[HF DISCOVERY] Starting discovery for {request.Repository}");

        // Step 1: Fetch basic dataset info from HuggingFace Hub
        HuggingFaceDatasetInfo? info = await _huggingFaceClient.GetDatasetInfoAsync(
            request.Repository,
            request.Revision,
            request.AccessToken,
            cancellationToken);

        if (info == null)
        {
            Logs.Warning($"[HF DISCOVERY] Dataset {request.Repository} not found or inaccessible");
            return new HuggingFaceDiscoveryResponse
            {
                Repository = request.Repository,
                IsAccessible = false,
                ErrorMessage = "Dataset not found or inaccessible on HuggingFace Hub"
            };
        }

        Logs.Info($"[HF DISCOVERY] Found dataset {request.Repository} with {info.Files.Count} files");

        // Build dataset profile
        HuggingFaceDatasetProfile profile = HuggingFaceDatasetProfile.FromDatasetInfo(request.Repository, info);

        // Step 2: Build metadata
        HuggingFaceDatasetMetadata metadata = new HuggingFaceDatasetMetadata
        {
            Id = info.Id,
            Author = info.Author,
            IsPrivate = info.Private,
            IsGated = info.Gated,
            Tags = info.Tags,
            FileCount = info.Files.Count
        };

        // Step 3: Discover streaming options (if requested)
        HuggingFaceStreamingOptions? streamingOptions = null;
        if (request.IsStreaming)
        {
            Logs.Info($"[HF DISCOVERY] Discovering streaming options for {request.Repository}");
            streamingOptions = await DiscoverStreamingOptionsAsync(
                request.Repository,
                request.AccessToken,
                cancellationToken);
        }

        // Step 4: Build download options
        HuggingFaceDownloadOptions downloadOptions = BuildDownloadOptions(profile);

        Logs.Info($"[HF DISCOVERY] Discovery complete for {request.Repository}");

        return new HuggingFaceDiscoveryResponse
        {
            Repository = request.Repository,
            IsAccessible = true,
            Metadata = metadata,
            StreamingOptions = streamingOptions,
            DownloadOptions = downloadOptions
        };
    }

    private async Task<HuggingFaceStreamingOptions> DiscoverStreamingOptionsAsync(
        string repository,
        string? accessToken,
        CancellationToken cancellationToken)
    {
        try
        {
            // Get ALL available config/split combinations
            List<HuggingFaceDatasetSplitInfo>? allSplits = await _datasetServerClient.GetAllSplitsAsync(
                repository,
                accessToken,
                cancellationToken);

            if (allSplits != null && allSplits.Count > 0)
            {
                Logs.Info($"[HF DISCOVERY] Found {allSplits.Count} config/split combinations for {repository}");
                
                // Convert to HuggingFaceConfigOption
                List<HuggingFaceConfigOption> options = new List<HuggingFaceConfigOption>();
                
                foreach (HuggingFaceDatasetSplitInfo splitInfo in allSplits)
                {
                    options.Add(new HuggingFaceConfigOption
                    {
                        Config = splitInfo.Config,
                        Split = splitInfo.Split,
                        NumRows = splitInfo.NumRows,
                        IsRecommended = false,
                        DisplayLabel = FormatConfigOptionLabel(splitInfo.Config, splitInfo.Split, splitInfo.NumRows)
                    });
                }

                // Determine recommended option using heuristics
                HuggingFaceConfigOption? recommended = DetermineRecommendedOption(options);
                if (recommended != null)
                {
                    recommended.IsRecommended = true;
                }

                return new HuggingFaceStreamingOptions
                {
                    IsSupported = true,
                    RecommendedOption = recommended ?? options[0],
                    AvailableOptions = options
                };
            }

            // Try rows probe
            HuggingFaceRowsPage? probePage = await _datasetServerClient.GetRowsAsync(
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
                
                HuggingFaceConfigOption option = new HuggingFaceConfigOption
                {
                    Config = probePage.Config,
                    Split = split,
                    NumRows = probePage.NumRowsTotal,
                    IsRecommended = true,
                    DisplayLabel = FormatConfigOptionLabel(probePage.Config, split, probePage.NumRowsTotal)
                };

                return new HuggingFaceStreamingOptions
                {
                    IsSupported = true,
                    RecommendedOption = option,
                    AvailableOptions = new List<HuggingFaceConfigOption> { option }
                };
            }

            return new HuggingFaceStreamingOptions
            {
                IsSupported = false,
                UnsupportedReason = "datasets-server /size and /rows endpoints did not return usable data"
            };
        }
        catch (Exception ex)
        {
            Logs.Warning($"[HF DISCOVERY] Error discovering streaming options: {ex.Message}");
            return new HuggingFaceStreamingOptions
            {
                IsSupported = false,
                UnsupportedReason = $"Error probing datasets-server: {ex.Message}"
            };
        }
    }

    private static HuggingFaceDownloadOptions BuildDownloadOptions(HuggingFaceDatasetProfile profile)
    {
        if (!profile.HasDataFiles && !profile.HasImageFiles)
        {
            return new HuggingFaceDownloadOptions
            {
                IsAvailable = false
            };
        }

        if (!profile.HasDataFiles && profile.HasImageFiles)
        {
            return new HuggingFaceDownloadOptions
            {
                IsAvailable = true,
                HasImageFilesOnly = true,
                ImageFileCount = profile.ImageFiles.Count
            };
        }

        List<HuggingFaceDataFileOption> fileOptions = profile.DataFiles
            .Select((file, index) => new HuggingFaceDataFileOption
            {
                Path = file.Path,
                Type = file.Type,
                Size = file.Size,
                IsPrimary = index == 0
            })
            .ToList();

        return new HuggingFaceDownloadOptions
        {
            IsAvailable = true,
            PrimaryFile = fileOptions.FirstOrDefault(f => f.IsPrimary),
            AvailableFiles = fileOptions,
            HasImageFilesOnly = false,
            ImageFileCount = profile.ImageFiles.Count
        };
    }

    private static HuggingFaceConfigOption? DetermineRecommendedOption(List<HuggingFaceConfigOption> options)
    {
        if (options.Count == 0)
            return null;

        if (options.Count == 1)
            return options[0];

        // Heuristics to pick the best option:
        // 1. Prefer config names containing "random_1k" or "small" (manageable size for demos)
        // 2. Prefer "train" split over others
        // 3. Prefer smaller row counts (faster initial load)
        
        HuggingFaceConfigOption? best = null;
        int bestScore = int.MinValue;

        foreach (HuggingFaceConfigOption option in options)
        {
            int score = 0;

            // Prefer configs with "random_1k", "small", "tiny"
            string configLower = option.Config?.ToLowerInvariant() ?? "";
            if (configLower.Contains("random_1k") || configLower.Contains("1k"))
                score += 100;
            else if (configLower.Contains("small"))
                score += 50;
            else if (configLower.Contains("tiny"))
                score += 40;

            // Prefer "train" split
            if (string.Equals(option.Split, "train", StringComparison.OrdinalIgnoreCase))
                score += 30;

            // Prefer smaller datasets (inverse of size)
            if (option.NumRows.HasValue && option.NumRows.Value > 0)
            {
                // Prefer datasets under 10K rows
                if (option.NumRows.Value <= 10_000)
                    score += 20;
                else if (option.NumRows.Value <= 100_000)
                    score += 10;
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = option;
            }
        }

        return best ?? options[0];
    }

    private static string FormatConfigOptionLabel(string? config, string split, long? numRows)
    {
        string label = string.IsNullOrWhiteSpace(config) ? split : $"{config} / {split}";
        
        if (numRows.HasValue)
        {
            label += $" ({FormatRowCount(numRows.Value)} rows)";
        }

        return label;
    }

    private static string FormatRowCount(long count)
    {
        if (count >= 1_000_000)
        {
            return $"{count / 1_000_000.0:F1}M";
        }
        else if (count >= 1_000)
        {
            return $"{count / 1_000.0:F1}K";
        }
        else
        {
            return count.ToString();
        }
    }
}

