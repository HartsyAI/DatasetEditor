using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using DatasetStudio.APIBackend.Extensions;
using DatasetStudio.APIBackend.Models;
using DatasetStudio.APIBackend.Services.DatasetManagement;
using DatasetStudio.APIBackend.Services.DatasetManagement.Dtos;
using DatasetStudio.APIBackend.Services.Integration;
using DatasetStudio.DTO.Common;
using DatasetStudio.DTO.Datasets;

namespace DatasetStudio.APIBackend.Endpoints;

/// <summary>Dataset management endpoints</summary>
internal static class DatasetEndpoints
{
    /// <summary>Maps all dataset endpoints</summary>
    internal static void MapDatasetEndpoints(this WebApplication app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/datasets").WithTags("Datasets");

        group.MapPost("/huggingface/discover", DiscoverHuggingFaceDataset)
            .WithName("DiscoverHuggingFaceDataset")
            .Produces<Integration.HuggingFaceDiscoveryResponse>()
            .Produces(StatusCodes.Status400BadRequest);

        group.MapGet("/", GetAllDatasets)
            .WithName("GetAllDatasets")
            .Produces<object>();

        group.MapGet("/{datasetId:guid}", GetDataset)
            .WithName("GetDataset")
            .Produces<DatasetDetailDto>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateDataset)
            .WithName("CreateDataset")
            .Produces<DatasetDetailDto>(StatusCodes.Status201Created);

        group.MapPost("/{datasetId:guid}/upload", UploadDatasetFile)
            .Accepts<IFormFile>("multipart/form-data")
            .DisableAntiforgery()
            .WithName("UploadDatasetFile")
            .Produces(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapDelete("/{datasetId:guid}", DeleteDataset)
            .WithName("DeleteDataset")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{datasetId:guid}/items", GetDatasetItems)
            .WithName("ListDatasetItems")
            .Produces<PageResponse<DatasetItemDto>>();

        group.MapPost("/{datasetId:guid}/import-huggingface", ImportFromHuggingFace)
            .WithName("ImportFromHuggingFace")
            .Produces(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapGet("/{datasetId:guid}/files/{*filePath}", ServeDatasetFile)
            .WithName("ServeDatasetFile")
            .Produces<FileStreamResult>(StatusCodes.Status200OK, "image/jpeg", "image/png", "image/webp", "image/gif", "image/bmp")
            .Produces(StatusCodes.Status404NotFound);
    }

    /// <summary>Gets all datasets with pagination</summary>
    public static async Task<IResult> GetAllDatasets(
        IDatasetRepository datasetRepository,
        [FromQuery] int page = 0,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        // Get paginated datasets
        IReadOnlyList<DatasetEntity> allDatasets = await datasetRepository.ListAsync(cancellationToken);
        
        // Apply pagination
        List<DatasetEntity> pagedDatasets = allDatasets
            .Skip(page * pageSize)
            .Take(pageSize)
            .ToList();
        
        // Map to DTOs
        List<DatasetSummaryDto> dtos = pagedDatasets.Select(d => new DatasetSummaryDto
        {
            Id = d.Id,
            Name = d.Name,
            Description = d.Description,
            Status = d.Status,
            TotalItems = d.TotalItems,
            CreatedAt = d.CreatedAt,
            UpdatedAt = d.UpdatedAt,
            Format = "CSV", // Default format
            Modality = "Image" // Default modality
        }).ToList();
        
        return Results.Ok(new
        {
            datasets = dtos,
            totalCount = allDatasets.Count,
            page,
            pageSize
        });
    }

    /// <summary>Gets a single dataset by ID</summary>
    public static async Task<IResult> GetDataset(
        Guid datasetId,
        IDatasetRepository repository,
        CancellationToken cancellationToken)
    {
        DatasetEntity? dataset = await repository.GetAsync(datasetId, cancellationToken);
        
        if (dataset is null)
        {
            return Results.NotFound();
        }
        
        return Results.Ok(dataset.ToDetailDto());
    }

    /// <summary>Creates a new dataset</summary>
    public static async Task<IResult> CreateDataset(
        CreateDatasetRequest request,
        IDatasetRepository repository,
        IDatasetIngestionService ingestionService,
        CancellationToken cancellationToken)
    {
        DatasetEntity entity = new()
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            Status = IngestionStatusDto.Pending,
        };
        
        await repository.CreateAsync(entity, cancellationToken);
        await ingestionService.StartIngestionAsync(entity.Id, uploadLocation: null, cancellationToken);
        
        return Results.Created($"/api/datasets/{entity.Id}", entity.ToDetailDto());
    }

    /// <summary>Deletes a dataset and all of its items.</summary>
    public static async Task<IResult> DeleteDataset(
        Guid datasetId,
        IDatasetRepository datasetRepository,
        IDatasetItemRepository itemRepository,
        CancellationToken cancellationToken)
    {
        DatasetEntity? dataset = await datasetRepository.GetAsync(datasetId, cancellationToken);
        if (dataset is null)
        {
            return Results.NotFound();
        }

        await itemRepository.DeleteByDatasetAsync(datasetId, cancellationToken);
        await datasetRepository.DeleteAsync(datasetId, cancellationToken);

        return Results.NoContent();
    }

    /// <summary>Uploads a file to a dataset</summary>
    public static async Task<IResult> UploadDatasetFile(
        Guid datasetId,
        IFormFile file,
        IDatasetRepository repository,
        IDatasetIngestionService ingestionService,
        CancellationToken cancellationToken)
    {
        DatasetEntity? dataset = await repository.GetAsync(datasetId, cancellationToken);
        
        if (dataset is null)
        {
            return Results.NotFound();
        }
        
        if (file is null || file.Length == 0)
        {
            return Results.BadRequest("No file uploaded or file is empty.");
        }
        
        string tempFilePath = Path.Combine(
            Path.GetTempPath(),
            $"dataset-{datasetId}-{Guid.NewGuid()}{Path.GetExtension(file.FileName)}");
        
        await using (FileStream stream = File.Create(tempFilePath))
        {
            await file.CopyToAsync(stream, cancellationToken);
        }
        
        dataset.SourceFileName = file.FileName;
        await repository.UpdateAsync(dataset, cancellationToken);
        await ingestionService.StartIngestionAsync(datasetId, tempFilePath, cancellationToken);
        
        return Results.Accepted($"/api/datasets/{datasetId}", new { datasetId, fileName = file.FileName });
    }

    /// <summary>Gets items for a dataset with pagination</summary>
    public static async Task<IResult> GetDatasetItems(
        Guid datasetId,
        int? pageSize,
        string? cursor,
        IDatasetRepository datasetRepository,
        IDatasetItemRepository itemRepository,
        Integration.IHuggingFaceDatasetServerClient huggingFaceDatasetServerClient,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        DatasetEntity? dataset = await datasetRepository.GetAsync(datasetId, cancellationToken);
        if (dataset is null)
        {
            return Results.NotFound();
        }

        int size = pageSize.GetValueOrDefault(100);

        if (dataset.SourceType == DatasetSourceType.HuggingFaceStreaming || dataset.IsStreaming)
        {
            string? repository = dataset.HuggingFaceRepository;
            if (string.IsNullOrWhiteSpace(repository))
            {
                return Results.BadRequest(new { error = "HuggingFaceStreaming dataset is missing repository metadata" });
            }

            string? config = dataset.HuggingFaceConfig;
            string? split = dataset.HuggingFaceSplit;

            if (string.IsNullOrWhiteSpace(split))
            {
                Integration.HuggingFaceDatasetSizeInfo? sizeInfo = await huggingFaceDatasetServerClient.GetDatasetSizeAsync(
                    repository,
                    config,
                    split,
                    null,
                    cancellationToken);

                if (sizeInfo != null)
                {
                    config = sizeInfo.Config;
                    split = string.IsNullOrWhiteSpace(sizeInfo.Split) ? "train" : sizeInfo.Split;
                    dataset.HuggingFaceConfig = config;
                    dataset.HuggingFaceSplit = split;
                    if (sizeInfo.NumRows.HasValue)
                    {
                        dataset.TotalItems = sizeInfo.NumRows.Value;
                    }

                    await datasetRepository.UpdateAsync(dataset, cancellationToken);
                }
                else
                {
                    split = "train";
                }
            }

            int offset = 0;
            if (!string.IsNullOrWhiteSpace(cursor))
            {
                int parsedCursor;
                if (int.TryParse(cursor, out parsedCursor) && parsedCursor >= 0)
                {
                    offset = parsedCursor;
                }
            }

            StringValues headerValues = httpContext.Request.Headers["X-HF-Access-Token"];
            string? accessToken = headerValues.Count > 0 ? headerValues[0] : null;

            Integration.Integration.HuggingFaceRowsPage? page = await huggingFaceDatasetServerClient.GetRowsAsync(
                repository,
                config,
                split!,
                offset,
                size,
                accessToken,
                cancellationToken);

            if (page == null)
            {
                PageResponse<DatasetItemDto> emptyResponse = new PageResponse<DatasetItemDto>
                {
                    Items = Array.Empty<DatasetItemDto>(),
                    NextCursor = null,
                    TotalCount = 0
                };

                return Results.Ok(emptyResponse);
            }

            List<DatasetItemDto> mappedItems = new List<DatasetItemDto>(page.Rows.Count);
            foreach (Integration.HuggingFaceRow row in page.Rows)
            {
                DatasetItemDto item = MapStreamingRowToDatasetItem(datasetId, row, repository, config, split);
                mappedItems.Add(item);
            }

            long totalRows = page.NumRowsTotal;
            string? nextCursor = null;
            long nextOffset = (long)offset + mappedItems.Count;
            if (nextOffset < totalRows)
            {
                nextCursor = nextOffset.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            PageResponse<DatasetItemDto> streamingResponse = new PageResponse<DatasetItemDto>
            {
                Items = mappedItems,
                NextCursor = nextCursor,
                TotalCount = totalRows
            };

            return Results.Ok(streamingResponse);
        }

        (IReadOnlyList<DatasetItemDto> items, string? repositoryNextCursor) = await itemRepository.GetPageAsync(
            datasetId,
            null,
            cursor,
            size,
            cancellationToken);

        PageResponse<DatasetItemDto> response = new PageResponse<DatasetItemDto>
        {
            Items = items,
            NextCursor = repositoryNextCursor,
            TotalCount = null
        };

        return Results.Ok(response);
    }

    private static DatasetItemDto MapStreamingRowToDatasetItem(Guid datasetId, Integration.HuggingFaceRow row, string repository, string? config, string? split)
    {
        Dictionary<string, object?> values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (KeyValuePair<string, System.Text.Json.JsonElement> column in row.Columns)
        {
            object? converted = ConvertJsonElementToObject(column.Value);
            values[column.Key] = converted;
        }

        string externalId = GetFirstNonEmptyString(values, "id", "image_id", "uid", "uuid", "__key", "sample_id") ?? string.Empty;
        string? title = GetFirstNonEmptyString(values, "title", "caption", "text", "description", "label", "name");
        string? description = GetFirstNonEmptyString(values, "description", "caption", "text");
        string? imageUrl = GetFirstNonEmptyString(values, "image_url", "img_url", "url");

        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            foreach (KeyValuePair<string, object?> entry in values)
            {
                if (entry.Value == null)
                {
                    continue;
                }

                string candidate = entry.Value.ToString() ?? string.Empty;
                if (IsLikelyImageUrl(candidate))
                {
                    imageUrl = candidate;
                    break;
                }
            }
        }

        int width = GetIntValue(values, "width", "image_width", "w");
        int height = GetIntValue(values, "height", "image_height", "h");

        List<string> tags = new List<string>();
        string? tagsValue = GetFirstNonEmptyString(values, "tags", "labels");
        if (!string.IsNullOrWhiteSpace(tagsValue))
        {
            string[] parts = tagsValue.Split(new string[] { ",", ";" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                string trimmed = part.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    tags.Add(trimmed);
                }
            }
        }

        Dictionary<string, string> metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, object?> entry in values)
        {
            if (entry.Value == null)
            {
                continue;
            }

            string stringValue = entry.Value.ToString() ?? string.Empty;
            metadata[entry.Key] = stringValue;
        }

        metadata["hf_repository"] = repository;
        if (!string.IsNullOrWhiteSpace(config))
        {
            metadata["hf_config"] = config;
        }
        if (!string.IsNullOrWhiteSpace(split))
        {
            metadata["hf_split"] = split;
        }

        DateTime now = DateTime.UtcNow;

        DatasetItemDto dto = new DatasetItemDto
        {
            Id = Guid.NewGuid(),
            DatasetId = datasetId,
            ExternalId = externalId,
            Title = string.IsNullOrWhiteSpace(title) ? externalId : title,
            Description = description,
            ImageUrl = string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl,
            ThumbnailUrl = string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl,
            Width = width,
            Height = height,
            Tags = tags,
            IsFavorite = false,
            Metadata = metadata,
            CreatedAt = now,
            UpdatedAt = now
        };

        return dto;
    }

    private static object? ConvertJsonElementToObject(System.Text.Json.JsonElement element)
    {
        switch (element.ValueKind)
        {
            case System.Text.Json.JsonValueKind.String:
                return element.GetString();
            case System.Text.Json.JsonValueKind.Object:
                if (element.TryGetProperty("src", out System.Text.Json.JsonElement srcProperty) &&
                    srcProperty.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    return srcProperty.GetString();
                }

                return element.ToString();
            case System.Text.Json.JsonValueKind.Number:
                long longValue;
                if (element.TryGetInt64(out longValue))
                {
                    return longValue;
                }

                double doubleValue;
                if (element.TryGetDouble(out doubleValue))
                {
                    return doubleValue;
                }

                return element.ToString();
            case System.Text.Json.JsonValueKind.True:
            case System.Text.Json.JsonValueKind.False:
                return element.GetBoolean();
            case System.Text.Json.JsonValueKind.Null:
            case System.Text.Json.JsonValueKind.Undefined:
                return null;
            default:
                return element.ToString();
        }
    }

    private static string? GetFirstNonEmptyString(IReadOnlyDictionary<string, object?> values, params string[] keys)
    {
        foreach (string key in keys)
        {
            object? value;
            if (values.TryGetValue(key, out value) && value != null)
            {
                string stringValue = value.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(stringValue))
                {
                    return stringValue;
                }
            }
        }

        return null;
    }

    private static int GetIntValue(IReadOnlyDictionary<string, object?> values, params string[] keys)
    {
        foreach (string key in keys)
        {
            object? value;
            if (values.TryGetValue(key, out value) && value != null)
            {
                int intValue;
                if (value is int)
                {
                    intValue = (int)value;
                    return intValue;
                }

                if (int.TryParse(value.ToString(), out intValue))
                {
                    return intValue;
                }
            }
        }

        return 0;
    }

    private static bool IsLikelyImageUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string lower = value.ToLowerInvariant();
        if (!lower.Contains("http", StringComparison.Ordinal))
        {
            return false;
        }

        return lower.EndsWith(".jpg", StringComparison.Ordinal) ||
               lower.EndsWith(".jpeg", StringComparison.Ordinal) ||
               lower.EndsWith(".png", StringComparison.Ordinal) ||
               lower.EndsWith(".webp", StringComparison.Ordinal) ||
               lower.EndsWith(".gif", StringComparison.Ordinal) ||
               lower.EndsWith(".bmp", StringComparison.Ordinal);
    }

    /// <summary>Imports a dataset from HuggingFace Hub</summary>
    public static async Task<IResult> ImportFromHuggingFace(
        Guid datasetId,
        ImportHuggingFaceDatasetRequest request,
        IDatasetRepository repository,
        IDatasetIngestionService ingestionService,
        CancellationToken cancellationToken)
    {
        DatasetEntity? dataset = await repository.GetAsync(datasetId, cancellationToken);

        if (dataset is null)
        {
            return Results.NotFound(new { error = "Dataset not found" });
        }

        if (string.IsNullOrWhiteSpace(request.Repository))
        {
            return Results.BadRequest(new { error = "Repository name is required" });
        }

        // Update dataset name/description if provided
        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            dataset.Name = request.Name;
        }
        if (!string.IsNullOrWhiteSpace(request.Description))
        {
            dataset.Description = request.Description;
        }

        await repository.UpdateAsync(dataset, cancellationToken);

        // Start import in background (don't await)
        _ = Task.Run(async () =>
        {
            try
            {
                await ingestionService.ImportFromHuggingFaceAsync(datasetId, request, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"HuggingFace import failed: {ex.Message}");
            }
        }, CancellationToken.None);

        return Results.Accepted($"/api/datasets/{datasetId}", new
        {
            datasetId,
            repository = request.Repository,
            isStreaming = request.IsStreaming,
            message = "Import started. Check dataset status for progress."
        });
    }

    /// <summary>Serves a file from a dataset's folder (for locally stored images)</summary>
    public static async Task<IResult> ServeDatasetFile(
        Guid datasetId,
        string filePath,
        IDatasetRepository datasetRepository,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        DatasetEntity? dataset = await datasetRepository.GetAsync(datasetId, cancellationToken);
        if (dataset is null)
        {
            return Results.NotFound();
        }

        // Get dataset root path from configuration
        string datasetRootPath = configuration["Storage:DatasetRootPath"]
            ?? Path.Combine(AppContext.BaseDirectory, "data", "datasets");

        // Build the dataset folder path
        string datasetFolder = GetDatasetFolderPathForFile(dataset, datasetRootPath);

        // Build the full file path
        string fullPath = Path.Combine(datasetFolder, filePath);
        string normalizedFullPath = Path.GetFullPath(fullPath);
        string normalizedDatasetFolder = Path.GetFullPath(datasetFolder);

        // Security check: ensure the file is within the dataset folder
        if (!normalizedFullPath.StartsWith(normalizedDatasetFolder, StringComparison.OrdinalIgnoreCase))
        {
            return Results.NotFound();
        }

        if (!File.Exists(normalizedFullPath))
        {
            return Results.NotFound();
        }

        // Determine content type based on file extension
        string extension = Path.GetExtension(normalizedFullPath).ToLowerInvariant();
        string contentType = extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            _ => "application/octet-stream"
        };

        FileStream fileStream = File.OpenRead(normalizedFullPath);
        return Results.File(fileStream, contentType, enableRangeProcessing: true);
    }

    private static string GetDatasetFolderPathForFile(DatasetEntity dataset, string datasetRootPath)
    {
        string root = Path.GetFullPath(datasetRootPath);
        Directory.CreateDirectory(root);

        string slug = Slugify(dataset.Name);
        string shortId = dataset.Id.ToString("N")[..8];
        string folderName = $"{slug}-{shortId}";
        string datasetFolder = Path.Combine(root, folderName);

        return datasetFolder;
    }

    private static string Slugify(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "dataset";
        }

        value = value.Trim().ToLowerInvariant();
        System.Text.StringBuilder sb = new(value.Length);
        bool previousDash = false;

        foreach (char c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
                previousDash = false;
            }
            else if (c == ' ' || c == '-' || c == '_' || c == '.')
            {
                if (!previousDash && sb.Length > 0)
                {
                    sb.Append('-');
                    previousDash = true;
                }
            }
        }

        if (sb.Length == 0)
        {
            return "dataset";
        }

        if (sb[^1] == '-')
        {
            sb.Length--;
        }

        return sb.ToString();
    }

    /// <summary>Discovers available configs, splits, and files for a HuggingFace dataset</summary>
    public static async Task<IResult> DiscoverHuggingFaceDataset(
        [FromBody] Integration.HuggingFaceDiscoveryRequest request,
        Integration.IHuggingFaceDiscoveryService discoveryService,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Repository))
        {
            return Results.BadRequest(new { error = "Repository name is required" });
        }

        Integration.HuggingFaceDiscoveryResponse response = await discoveryService.DiscoverDatasetAsync(
            request,
            cancellationToken);

        return Results.Ok(response);
    }
}

