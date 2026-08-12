using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using DatasetStudio.APIBackend.Extensions;
using DatasetStudio.APIBackend.Models;
using DatasetStudio.APIBackend.Services.Auth;
using DatasetStudio.APIBackend.Services.DatasetManagement;
using DatasetStudio.APIBackend.Services.DatasetManagement.Dtos;
using DatasetStudio.APIBackend.Services.Integration;
using DatasetStudio.DTO.Common;
using DatasetStudio.DTO.Datasets;
using DatasetVisibilityFilter = DatasetStudio.Core.Abstractions.Repositories.DatasetVisibilityFilter;

// IDatasetRepository is referenced fully-qualified (Core.Abstractions.Repositories.IDatasetRepository)
// rather than via `using`, because this file also uses the unrelated, same-named
// Services.DatasetManagement.IDatasetItemRepository — a `using` for both namespaces would make
// IDatasetItemRepository ambiguous. DatasetIngestionService follows the same convention.
// DatasetVisibilityFilter is aliased individually instead for the same reason.

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
            .Produces<HuggingFaceDiscoveryResponse>()
            .Produces(StatusCodes.Status400BadRequest);

        // Anonymous-reachable, but each handler below scopes its own results/checks to what
        // the caller may actually see (IDatasetPermissionService / DatasetVisibilityFilter) --
        // a private dataset is invisible to a stranger, not 401/403'd, matching how e.g. GitHub
        // treats a private repo you're not a collaborator on (404, not "exists but forbidden").
        group.MapGet("/", GetAllDatasets)
            .WithName("GetAllDatasets")
            .Produces<object>();

        group.MapGet("/{datasetId:guid}", GetDataset)
            .WithName("GetDataset")
            .Produces<DatasetDetailDto>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateDataset)
            .WithName("CreateDataset")
            .Produces<DatasetDetailDto>(StatusCodes.Status201Created)
            .RequireAuthorization();

        group.MapPost("/{datasetId:guid}/upload", UploadDatasetFile)
            .Accepts<IFormFile>("multipart/form-data")
            .DisableAntiforgery()
            .WithName("UploadDatasetFile")
            .Produces(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        group.MapDelete("/{datasetId:guid}", DeleteDataset)
            .WithName("DeleteDataset")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        group.MapGet("/{datasetId:guid}/items", GetDatasetItems)
            .WithName("ListDatasetItems")
            .Produces<PageResponse<DatasetItemDto>>();

        group.MapPost("/{datasetId:guid}/import-huggingface", ImportFromHuggingFace)
            .WithName("ImportFromHuggingFace")
            .Produces(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        // Admin-only, not just "any authenticated user with write access" -- this ingests an
        // arbitrary path off the server's own filesystem. Requiring mere authentication would
        // let any self-registered account (registration is open) use the server as a proxy to
        // read/enumerate any folder the API process can see.
        group.MapPost("/{datasetId:guid}/import-folder", ImportFromFolder)
            .WithName("ImportFromFolder")
            .Produces(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest)
            .RequireAuthorization(policy => policy.RequireRole("Admin"));

        group.MapGet("/{datasetId:guid}/files/{*filePath}", ServeDatasetFile)
            .WithName("ServeDatasetFile")
            .Produces<FileStreamResult>(StatusCodes.Status200OK, "image/jpeg", "image/png", "image/webp", "image/gif", "image/bmp")
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{datasetId:guid}/hf-thumbnail", GetHuggingFaceThumbnail)
            .WithName("GetHuggingFaceThumbnail")
            .Produces<FileStreamResult>(StatusCodes.Status200OK, "image/jpeg")
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest);
    }

    /// <summary>Gets all datasets with pagination, scoped to what the caller may see.</summary>
    public static async Task<IResult> GetAllDatasets(
        [FromServices] Core.Abstractions.Repositories.IDatasetRepository datasetRepository,
        HttpContext httpContext,
        [FromQuery] int page = 0,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        DatasetVisibilityFilter visibility = BuildVisibilityFilter(httpContext.User);
        List<DatasetDto> pagedDatasets = await datasetRepository.GetAllAsync(page, pageSize, visibility, cancellationToken);
        long totalCount = await datasetRepository.GetCountAsync(visibility, cancellationToken);

        List<DatasetSummaryDto> dtos = pagedDatasets.Select(d => d.ToSummaryDto()).ToList();

        return Results.Ok(new
        {
            datasets = dtos,
            totalCount,
            page,
            pageSize
        });
    }

    /// <summary>Gets a single dataset by ID. 404s (not 403) if the caller can't see it -- existence of a private dataset isn't revealed to non-owners.</summary>
    public static async Task<IResult> GetDataset(
        Guid datasetId,
        Core.Abstractions.Repositories.IDatasetRepository repository,
        IDatasetPermissionService permissionService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        DatasetDto? dataset = await repository.GetByIdAsync(datasetId, cancellationToken);

        if (dataset is null || !await permissionService.CanReadAsync(dataset, httpContext.User, cancellationToken))
        {
            return Results.NotFound();
        }

        return Results.Ok(dataset.ToDetailDto());
    }

    /// <summary>Creates a new dataset, owned by the authenticated caller.</summary>
    public static async Task<IResult> CreateDataset(
        CreateDatasetRequest request,
        Core.Abstractions.Repositories.IDatasetRepository repository,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        // RequireAuthorization() on this route guarantees GetUserId() is non-null here.
        DatasetDto dataset = new()
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            Status = IngestionStatusDto.Pending,
            SourceType = request.SourceType,
            SourceUri = request.SourceUri,
            IsStreaming = request.IsStreaming,
            CreatedByUserId = httpContext.User.GetUserId(),
        };

        Guid createdId = await repository.CreateAsync(dataset, cancellationToken);

        // Just creates the dataset shell in Pending status. Ingestion itself is kicked off
        // by a follow-up call — UploadDatasetFile (local file) or ImportFromHuggingFace —
        // once there's actually something to ingest. There is deliberately no
        // StartIngestionAsync(id, uploadLocation: null) call here: that method treats a
        // missing upload location as a failure and throws, which would 500 every create.
        DatasetDto created = dataset with { Id = createdId };

        return Results.Created($"/api/datasets/{createdId}", created.ToDetailDto());
    }

    /// <summary>Deletes a dataset and all of its items. Requires delete permission (owner, admin, or a granted CanDelete permission).</summary>
    public static async Task<IResult> DeleteDataset(
        Guid datasetId,
        Core.Abstractions.Repositories.IDatasetRepository datasetRepository,
        IDatasetItemRepository itemRepository,
        IDatasetPermissionService permissionService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        DatasetDto? dataset = await datasetRepository.GetByIdAsync(datasetId, cancellationToken);
        if (dataset is null)
        {
            return Results.NotFound();
        }
        // A caller who can't even read this dataset (private, not theirs, no grant) must get
        // the same 404 the read endpoints give -- otherwise a 403 here leaks "this private
        // dataset exists" to anyone who has (or guesses) its id, defeating the point of the
        // read side's not-found-means-hidden design.
        if (!await permissionService.CanReadAsync(dataset, httpContext.User, cancellationToken))
        {
            return Results.NotFound();
        }
        if (!await permissionService.CanDeleteAsync(dataset, httpContext.User, cancellationToken))
        {
            return Results.Forbid();
        }

        await itemRepository.DeleteByDatasetAsync(datasetId, cancellationToken);
        await datasetRepository.DeleteAsync(datasetId, cancellationToken);

        return Results.NoContent();
    }

    /// <summary>Uploads a file to a dataset. Requires write permission on the target dataset.</summary>
    public static async Task<IResult> UploadDatasetFile(
        Guid datasetId,
        IFormFile file,
        Core.Abstractions.Repositories.IDatasetRepository repository,
        IngestionQueue ingestionQueue,
        IDatasetPermissionService permissionService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        DatasetDto? dataset = await repository.GetByIdAsync(datasetId, cancellationToken);

        if (dataset is null)
        {
            return Results.NotFound();
        }
        // See DeleteDataset's identical guard: without this, a private dataset's existence
        // leaks to anyone who has its id via 403-vs-404, even though GetDataset hides it.
        if (!await permissionService.CanReadAsync(dataset, httpContext.User, cancellationToken))
        {
            return Results.NotFound();
        }
        if (!await permissionService.CanWriteAsync(dataset, httpContext.User, cancellationToken))
        {
            return Results.Forbid();
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

        DatasetDto updated = dataset with { SourceFileName = file.FileName };
        await repository.UpdateAsync(updated, cancellationToken);

        // Queue ingestion to run in the background; return immediately so a large upload
        // doesn't hold the request thread. The client polls dataset status for progress.
        ingestionQueue.Enqueue(new IngestionJob(datasetId, tempFilePath));

        return Results.Accepted($"/api/datasets/{datasetId}", new { datasetId, fileName = file.FileName });
    }

    /// <summary>Gets items for a dataset with pagination (supports both streaming and local)</summary>
    public static async Task<IResult> GetDatasetItems(
        Guid datasetId,
        int? pageSize,
        string? cursor,
        Core.Abstractions.Repositories.IDatasetRepository datasetRepository,
        IDatasetItemRepository itemRepository,
        IHuggingFaceDatasetServerClient huggingFaceDatasetServerClient,
        IDatasetPermissionService permissionService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        DatasetDto? dataset = await datasetRepository.GetByIdAsync(datasetId, cancellationToken);
        if (dataset is null || !await permissionService.CanReadAsync(dataset, httpContext.User, cancellationToken))
        {
            return Results.NotFound();
        }

        int size = pageSize.GetValueOrDefault(100);

        // Handle HuggingFace streaming datasets
        if (dataset.SourceType == DatasetSourceType.HuggingFaceStreaming || dataset.IsStreaming)
        {
            string? repository = dataset.HuggingFaceRepository;
            if (string.IsNullOrWhiteSpace(repository))
            {
                return Results.BadRequest(new { error = "HuggingFaceStreaming dataset is missing repository metadata" });
            }

            string? config = dataset.HuggingFaceConfig;
            string? split = dataset.HuggingFaceSplit;

            // Auto-discover config/split if not set
            if (string.IsNullOrWhiteSpace(split))
            {
                HuggingFaceDatasetSizeInfo? sizeInfo = await huggingFaceDatasetServerClient.GetDatasetSizeAsync(
                    repository,
                    config,
                    split,
                    null,
                    cancellationToken);

                if (sizeInfo != null)
                {
                    config = sizeInfo.Config;
                    split = string.IsNullOrWhiteSpace(sizeInfo.Split) ? "train" : sizeInfo.Split;
                    dataset = dataset with
                    {
                        HuggingFaceConfig = config,
                        HuggingFaceSplit = split,
                        TotalItems = sizeInfo.NumRows ?? dataset.TotalItems,
                    };

                    await datasetRepository.UpdateAsync(dataset, cancellationToken);
                }
                else
                {
                    split = "train";
                }
            }

            // Parse cursor as offset
            int offset = 0;
            if (!string.IsNullOrWhiteSpace(cursor) && int.TryParse(cursor, out int parsedCursor) && parsedCursor >= 0)
            {
                offset = parsedCursor;
            }

            // Get access token from header
            StringValues headerValues = httpContext.Request.Headers["X-HF-Access-Token"];
            string? accessToken = headerValues.Count > 0 ? headerValues[0] : null;

            // Fetch rows from HuggingFace datasets-server
            HuggingFaceRowsPage? page = await huggingFaceDatasetServerClient.GetRowsAsync(
                repository,
                config,
                split!,
                offset,
                size,
                accessToken,
                cancellationToken);

            if (page == null)
            {
                return Results.Ok(new PageResponse<DatasetItemDto>
                {
                    Items = Array.Empty<DatasetItemDto>(),
                    NextCursor = null,
                    TotalCount = 0
                });
            }

            // Map HuggingFace rows to DatasetItemDto
            List<DatasetItemDto> mappedItems = new List<DatasetItemDto>(page.Rows.Count);
            foreach (HuggingFaceRow row in page.Rows)
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

            return Results.Ok(new PageResponse<DatasetItemDto>
            {
                Items = mappedItems,
                NextCursor = nextCursor,
                TotalCount = totalRows
            });
        }

        // Handle local datasets (uploaded files)
        (IReadOnlyList<DatasetItemDto> items, string? repositoryNextCursor) = await itemRepository.GetPageAsync(
            datasetId,
            null,
            cursor,
            size,
            cancellationToken);

        return Results.Ok(new PageResponse<DatasetItemDto>
        {
            Items = items,
            NextCursor = repositoryNextCursor,
            TotalCount = null
        });
    }

    /// <summary>Maps a streaming HuggingFace row to DatasetItemDto</summary>
    private static DatasetItemDto MapStreamingRowToDatasetItem(Guid datasetId, HuggingFaceRow row, string repository, string? config, string? split)
    {
        Dictionary<string, object?> values = RowToValueDictionary(row);

        string externalId = GetFirstNonEmptyString(values, "id", "image_id", "uid", "uuid", "__key", "sample_id") ?? string.Empty;
        string? title = GetFirstNonEmptyString(values, "title", "caption", "text", "description", "label", "name");
        string? description = GetFirstNonEmptyString(values, "description", "caption", "text");
        string? imageUrl = ResolveStreamingImageUrl(values);

        int width = GetIntValue(values, "width", "image_width", "w");
        int height = GetIntValue(values, "height", "image_height", "h");

        List<string> tags = new List<string>();
        string? tagsValue = GetFirstNonEmptyString(values, "tags", "labels");
        if (!string.IsNullOrWhiteSpace(tagsValue))
        {
            string[] parts = tagsValue.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
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

        // ThumbnailUrl points at our own hf-thumbnail endpoint (which re-fetches this exact
        // row from HF's own trusted datasets-server rather than trusting a client-supplied
        // URL -- see hf-thumbnail's doc comment) instead of the raw HF asset URL, so ImageCard
        // gets a cached, width-bounded thumbnail instead of pulling the original every time.
        // Streaming items have no stable id across requests (Id above is a fresh Guid on every
        // single mapping call), so RowIndex is what makes the thumbnail cacheable/re-fetchable.
        string? thumbnailUrl = string.IsNullOrWhiteSpace(imageUrl)
            ? null
            : $"/api/datasets/{datasetId}/hf-thumbnail?offset={row.RowIndex}";

        return new DatasetItemDto
        {
            Id = Guid.NewGuid(),
            ExternalId = externalId,
            Title = string.IsNullOrWhiteSpace(title) ? externalId : title,
            Description = description,
            ImageUrl = string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl,
            ThumbnailUrl = thumbnailUrl,
            Width = width,
            Height = height,
            Tags = tags,
            IsFavorite = false,
            Metadata = metadata,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    /// <summary>Converts a HuggingFace row's raw JSON columns into a flat, easily queried value dictionary.</summary>
    private static Dictionary<string, object?> RowToValueDictionary(HuggingFaceRow row)
    {
        Dictionary<string, object?> values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, System.Text.Json.JsonElement> column in row.Columns)
        {
            values[column.Key] = ConvertJsonElementToObject(column.Value);
        }
        return values;
    }

    /// <summary>
    /// Resolves a streaming row's image URL the same way for both item mapping and the
    /// hf-thumbnail endpoint, so a thumbnail request always re-derives the exact URL the grid
    /// would have shown for that row.
    /// </summary>
    private static string? ResolveStreamingImageUrl(IReadOnlyDictionary<string, object?> values)
    {
        // "image" (and "img"/"picture") are HF's actual conventional column names for an
        // image feature (e.g. ylecun/mnist uses "image") — image_url/img_url/url are the
        // exception, not the rule, so real HF datasets fell through to the much weaker
        // extension-sniffing fallback below almost every time.
        string? imageUrl = GetFirstNonEmptyString(values, "image_url", "img_url", "url", "image", "img", "picture");

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

        return imageUrl;
    }

    /// <summary>Converts JsonElement to object</summary>
    private static object? ConvertJsonElementToObject(System.Text.Json.JsonElement element)
    {
        switch (element.ValueKind)
        {
            case System.Text.Json.JsonValueKind.String:
                return element.GetString();
            case System.Text.Json.JsonValueKind.Object:
                // Handle image objects with {src: "url"} format
                if (element.TryGetProperty("src", out System.Text.Json.JsonElement srcProperty) &&
                    srcProperty.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    return srcProperty.GetString();
                }
                return element.ToString();
            case System.Text.Json.JsonValueKind.Number:
                if (element.TryGetInt64(out long longValue))
                {
                    return longValue;
                }
                if (element.TryGetDouble(out double doubleValue))
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

    /// <summary>Gets first non-empty string from dictionary</summary>
    private static string? GetFirstNonEmptyString(IReadOnlyDictionary<string, object?> values, params string[] keys)
    {
        foreach (string key in keys)
        {
            if (values.TryGetValue(key, out object? value) && value != null)
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

    /// <summary>Gets int value from dictionary</summary>
    private static int GetIntValue(IReadOnlyDictionary<string, object?> values, params string[] keys)
    {
        foreach (string key in keys)
        {
            if (values.TryGetValue(key, out object? value) && value != null)
            {
                if (value is int intValue)
                {
                    return intValue;
                }

                if (int.TryParse(value.ToString(), out int parsed))
                {
                    return parsed;
                }
            }
        }
        return 0;
    }

    /// <summary>Checks if string is likely an image URL</summary>
    private static bool IsLikelyImageUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!value.Contains("http", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Check the URL's path, not the raw string tail — signed/CDN URLs (S3 presigned,
        // HF's own datasets-server cached-assets links, ...) are extremely common in the wild
        // and put a query string after the extension (".../image.jpg?Expires=...&Signature=
        // ..."), which a plain EndsWith check never matches. This is exactly what real
        // HuggingFace datasets-server image URLs look like, so the naive version silently
        // failed to recognize images on real datasets almost all the time.
        string pathPart = value;
        int queryIndex = pathPart.IndexOfAny(new[] { '?', '#' });
        if (queryIndex >= 0)
        {
            pathPart = pathPart[..queryIndex];
        }
        string lowerPath = pathPart.ToLowerInvariant();

        return lowerPath.EndsWith(".jpg", StringComparison.Ordinal) ||
               lowerPath.EndsWith(".jpeg", StringComparison.Ordinal) ||
               lowerPath.EndsWith(".png", StringComparison.Ordinal) ||
               lowerPath.EndsWith(".webp", StringComparison.Ordinal) ||
               lowerPath.EndsWith(".gif", StringComparison.Ordinal) ||
               lowerPath.EndsWith(".bmp", StringComparison.Ordinal);
    }

    /// <summary>Imports a dataset from HuggingFace Hub. Requires write permission on the target dataset.</summary>
    public static async Task<IResult> ImportFromHuggingFace(
        Guid datasetId,
        ImportHuggingFaceDatasetRequest request,
        Core.Abstractions.Repositories.IDatasetRepository repository,
        IngestionQueue ingestionQueue,
        IDatasetPermissionService permissionService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        DatasetDto? dataset = await repository.GetByIdAsync(datasetId, cancellationToken);

        if (dataset is null)
        {
            return Results.NotFound(new { error = "Dataset not found" });
        }
        // See DeleteDataset's identical guard: without this, a private dataset's existence
        // leaks to anyone who has its id via 403-vs-404, even though GetDataset hides it.
        if (!await permissionService.CanReadAsync(dataset, httpContext.User, cancellationToken))
        {
            return Results.NotFound(new { error = "Dataset not found" });
        }
        if (!await permissionService.CanWriteAsync(dataset, httpContext.User, cancellationToken))
        {
            return Results.Forbid();
        }

        if (string.IsNullOrWhiteSpace(request.Repository))
        {
            return Results.BadRequest(new { error = "Repository name is required" });
        }

        // Update dataset name/description if provided, and record the HF repository/config —
        // GetDatasetItems' streaming branch reads dataset.HuggingFaceRepository to know where
        // to stream rows from, so without this every streaming-mode dataset would complete
        // ingestion successfully and then fail every subsequent items request with "missing
        // repository metadata". This was invisible until DatasetRepository.UpdateAsync was
        // fixed to actually persist (see the AsTracking fix) — before that, no UpdateAsync call
        // anywhere persisted anything, so this specific gap couldn't manifest on its own.
        dataset = dataset with
        {
            Name = string.IsNullOrWhiteSpace(request.Name) ? dataset.Name : request.Name,
            Description = string.IsNullOrWhiteSpace(request.Description) ? dataset.Description : request.Description,
            SourceType = request.IsStreaming ? DatasetSourceType.HuggingFaceStreaming : DatasetSourceType.HuggingFace,
            IsStreaming = request.IsStreaming,
            HuggingFaceRepository = request.Repository,
            HuggingFaceConfig = request.Config,
            HuggingFaceSplit = request.Split,
        };

        await repository.UpdateAsync(dataset, cancellationToken);

        // Queued through the same background-worker path as file uploads, each job getting
        // its own DI scope — not a fire-and-forget Task.Run against a scoped
        // IDatasetIngestionService, which used to race the request's DI scope (and its
        // scoped DbContext) being disposed right after this handler returns.
        ingestionQueue.Enqueue(new IngestionJob(datasetId, FilePath: null, HuggingFaceRequest: request));

        return Results.Accepted($"/api/datasets/{datasetId}", new
        {
            datasetId,
            repository = request.Repository,
            isStreaming = request.IsStreaming,
            message = "Import started. Check dataset status for progress."
        });
    }

    /// <summary>
    /// Imports a dataset from a folder already on the server's disk. DatasetIngestionService's
    /// IngestFromFolderAsync has existed since before this endpoint did — it just had no route
    /// mapped to it, which is why the client's "Local folder" creation option was permanently
    /// labeled "(coming soon)".
    /// Still no path allow-listing at the ingestion layer -- this reads arbitrary server
    /// filesystem paths, so the route itself is gated to the site-wide Admin role (see
    /// MapDatasetEndpoints), not merely "authenticated". Requiring only authentication would
    /// let any self-registered account (registration is open) use the server as a proxy to
    /// read/enumerate any folder the API process can see.
    /// </summary>
    public static async Task<IResult> ImportFromFolder(
        Guid datasetId,
        ImportFolderRequest request,
        Core.Abstractions.Repositories.IDatasetRepository repository,
        IngestionQueue ingestionQueue,
        CancellationToken cancellationToken)
    {
        DatasetDto? dataset = await repository.GetByIdAsync(datasetId, cancellationToken);
        if (dataset is null)
        {
            return Results.NotFound(new { error = "Dataset not found" });
        }

        if (string.IsNullOrWhiteSpace(request.FolderPath))
        {
            return Results.BadRequest(new { error = "FolderPath is required" });
        }

        if (!Directory.Exists(request.FolderPath))
        {
            return Results.BadRequest(new { error = $"Folder not found: {request.FolderPath}" });
        }

        ingestionQueue.Enqueue(new IngestionJob(datasetId, FilePath: null, FolderPath: request.FolderPath));

        return Results.Accepted($"/api/datasets/{datasetId}", new
        {
            datasetId,
            folderPath = request.FolderPath,
            message = "Import started. Check dataset status for progress."
        });
    }

    /// <summary>
    /// Serves a file from a dataset's folder (for locally stored images). Same read-visibility
    /// rule as GetDataset. An optional <c>?w=</c> query param requests a cached, width-bounded
    /// JPEG thumbnail instead of the full-res original -- folded into this handler rather than
    /// a separate route so the resize path reuses the containment/symlink safety checks below
    /// as-is instead of a second copy of them (see docs/verification/phase-4/README.md).
    /// </summary>
    public static async Task<IResult> ServeDatasetFile(
        Guid datasetId,
        string filePath,
        Core.Abstractions.Repositories.IDatasetRepository datasetRepository,
        IDatasetPermissionService permissionService,
        Services.Storage.IThumbnailService thumbnailService,
        HttpContext httpContext,
        IConfiguration configuration,
        [FromQuery] int? w,
        CancellationToken cancellationToken)
    {
        DatasetDto? dataset = await datasetRepository.GetByIdAsync(datasetId, cancellationToken);
        if (dataset is null || !await permissionService.CanReadAsync(dataset, httpContext.User, cancellationToken))
        {
            return Results.NotFound();
        }

        // Datasets ingested from a local folder (StoragePath set) are served straight from
        // that folder — copying a potentially huge, arbitrarily-sized folder into managed
        // storage would defeat the point of "point at a folder on disk" ingestion. Everything
        // else (uploads, ZIP extraction) lives under the managed {root}/{slug}-{shortId}
        // convention instead.
        string datasetFolder;
        if (!string.IsNullOrWhiteSpace(dataset.StoragePath))
        {
            datasetFolder = Path.GetFullPath(dataset.StoragePath);
        }
        else
        {
            string datasetRootPath = configuration["Storage:DatasetRootPath"]
                ?? Path.Combine(AppContext.BaseDirectory, "data", "datasets");
            datasetFolder = Services.Storage.DatasetStorageConventions.GetDatasetFolderPath(
                datasetRootPath, dataset.Id, dataset.Name);
        }

        // Build the full file path
        string fullPath = Path.Combine(datasetFolder, filePath);
        string normalizedFullPath = Path.GetFullPath(fullPath);
        string normalizedDatasetFolder = Path.GetFullPath(datasetFolder);

        // Security check: ensure the file is within the dataset folder. The trailing
        // separator matters — without it, StartsWith("<root>/my-dataset-1234abcd") would also
        // match a sibling folder like "<root>/my-dataset-1234abcdEXTRA".
        string containmentPrefix = normalizedDatasetFolder.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedDatasetFolder
            : normalizedDatasetFolder + Path.DirectorySeparatorChar;
        if (!normalizedFullPath.StartsWith(containmentPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return Results.NotFound();
        }

        if (!File.Exists(normalizedFullPath))
        {
            return Results.NotFound();
        }

        // GetFullPath resolves ".." segments but not symlinks — a symlink placed inside the
        // dataset folder (e.g. by a malicious ZIP import; see the extraction-time guard in
        // DatasetIngestionService) could still point outside it and pass the check above.
        // Resolve the link chain and re-check containment against its real target.
        FileSystemInfo? linkTarget = new FileInfo(normalizedFullPath).ResolveLinkTarget(returnFinalTarget: true);
        if (linkTarget is not null)
        {
            string resolvedTargetPath = Path.GetFullPath(linkTarget.FullName);
            if (!resolvedTargetPath.StartsWith(containmentPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return Results.NotFound();
            }
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

        if (w.HasValue && w.Value > 0)
        {
            Services.Storage.ThumbnailResult thumbnail = await thumbnailService.GetOrCreateJpegAsync(
                normalizedFullPath,
                w.Value,
                async ct => await File.ReadAllBytesAsync(normalizedFullPath, ct),
                cancellationToken);

            // SourceNotFound can't happen here (File.Exists already confirmed above), but
            // handle it rather than assume. Undecodable falls through to the full-res
            // passthrough below with the file's real content type, not image/jpeg.
            if (thumbnail.Outcome == Services.Storage.ThumbnailOutcome.SourceNotFound)
            {
                return Results.NotFound();
            }
            if (thumbnail.Outcome == Services.Storage.ThumbnailOutcome.Generated)
            {
                return Results.File(thumbnail.Bytes!, "image/jpeg");
            }
            // Undecodable: fall through to serve the original below.
        }

        FileStream fileStream = File.OpenRead(normalizedFullPath);
        return Results.File(fileStream, contentType, enableRangeProcessing: true);
    }

    /// <summary>
    /// Serves a cached, width-bounded JPEG thumbnail for a HuggingFace-streaming item.
    /// Streaming items have no stable id across requests (a fresh Guid is minted on every
    /// single row-mapping call — see MapStreamingRowToDatasetItem), so this takes a row
    /// offset instead of an item id and re-fetches that exact row from HF's own trusted
    /// datasets-server API (via IHuggingFaceRowPageCache, batched per page) to resolve the
    /// image URL server-side. Deliberately does NOT accept a client-supplied image URL — that
    /// would make this endpoint an open proxy an attacker could point at an arbitrary internal
    /// or external host (SSRF); the only outbound fetch this handler ever makes is to a URL
    /// that datasets-server itself returned for this exact dataset/offset.
    /// </summary>
    public static async Task<IResult> GetHuggingFaceThumbnail(
        Guid datasetId,
        [FromQuery] long? offset,
        [FromQuery] int? w,
        Core.Abstractions.Repositories.IDatasetRepository datasetRepository,
        IDatasetPermissionService permissionService,
        IHuggingFaceDatasetServerClient huggingFaceDatasetServerClient,
        IHuggingFaceRowPageCache rowPageCache,
        Services.Storage.IThumbnailService thumbnailService,
        IHttpClientFactory httpClientFactory,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (offset is null || offset < 0)
        {
            return Results.BadRequest(new { error = "offset is required and must be >= 0" });
        }

        DatasetDto? dataset = await datasetRepository.GetByIdAsync(datasetId, cancellationToken);
        if (dataset is null || !await permissionService.CanReadAsync(dataset, httpContext.User, cancellationToken))
        {
            return Results.NotFound();
        }

        if (dataset.SourceType != DatasetSourceType.HuggingFaceStreaming && !dataset.IsStreaming)
        {
            return Results.NotFound();
        }

        string? repository = dataset.HuggingFaceRepository;
        string? split = dataset.HuggingFaceSplit;
        if (string.IsNullOrWhiteSpace(repository) || string.IsNullOrWhiteSpace(split))
        {
            // GetDatasetItems' streaming branch resolves and persists config/split on first
            // page load, before any item (and therefore any thumbnail URL) is ever handed to
            // the client -- so this only happens if hf-thumbnail is hit directly, out of order.
            return Results.NotFound();
        }
        string? config = dataset.HuggingFaceConfig;

        StringValues headerValues = httpContext.Request.Headers["X-HF-Access-Token"];
        string? accessToken = headerValues.Count > 0 ? headerValues[0] : null;

        HuggingFaceRow? row = await rowPageCache.GetRowAsync(
            huggingFaceDatasetServerClient, repository, config, split, offset.Value, accessToken, cancellationToken);
        if (row is null)
        {
            return Results.NotFound();
        }

        Dictionary<string, object?> values = RowToValueDictionary(row);
        string? imageUrl = ResolveStreamingImageUrl(values);
        if (string.IsNullOrWhiteSpace(imageUrl) || !Uri.TryCreate(imageUrl, UriKind.Absolute, out Uri? sourceUri)
            || (sourceUri.Scheme != Uri.UriSchemeHttp && sourceUri.Scheme != Uri.UriSchemeHttps))
        {
            return Results.NotFound();
        }

        int width = w.GetValueOrDefault(320);
        // Cache key folds in repository/config/split, not just datasetId+offset -- an existing
        // dataset row's HF repository/config can change on re-import (see
        // ImportFromHuggingFace), which would otherwise leave a stale cached thumbnail (the
        // old repository's image at this offset) permanently served under the new one.
        string cacheKey = $"hf|{repository}|{config}|{split}|{offset.Value}";

        Services.Storage.ThumbnailResult thumbnail = await thumbnailService.GetOrCreateJpegAsync(
            cacheKey,
            width,
            async ct =>
            {
                using HttpClient client = httpClientFactory.CreateClient(nameof(GetHuggingFaceThumbnail));
                client.Timeout = TimeSpan.FromSeconds(10);
                using HttpRequestMessage request = new(HttpMethod.Get, sourceUri);
                using HttpResponseMessage response = await client.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }
                return await response.Content.ReadAsByteArrayAsync(ct);
            },
            cancellationToken);

        return thumbnail.Outcome switch
        {
            Services.Storage.ThumbnailOutcome.Generated => Results.File(thumbnail.Bytes!, "image/jpeg"),
            // Undecodable: the upstream asset fetch succeeded but wasn't a real image (or HF
            // returned an error page) -- no original content-type to fall back to here like
            // ServeDatasetFile has, so surface it as not-found rather than guessing a type.
            _ => Results.NotFound(),
        };
    }

    /// <summary>Discovers available configs, splits, and files for a HuggingFace dataset</summary>
    public static async Task<IResult> DiscoverHuggingFaceDataset(
        [FromBody] HuggingFaceDiscoveryRequest request,
        IHuggingFaceDiscoveryService discoveryService,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Repository))
        {
            return Results.BadRequest(new { error = "Repository name is required" });
        }

        HuggingFaceDiscoveryResponse response = await discoveryService.DiscoverDatasetAsync(
            request,
            cancellationToken);

        return Results.Ok(response);
    }

    /// <summary>Derives a listing/count visibility scope from the current request's principal (anonymous, regular user, or admin).</summary>
    private static DatasetVisibilityFilter BuildVisibilityFilter(System.Security.Claims.ClaimsPrincipal user)
    {
        Guid? userId = user.GetUserId();
        return userId is null
            ? DatasetVisibilityFilter.ForAnonymous()
            : DatasetVisibilityFilter.ForUser(userId.Value, user.IsSiteAdmin());
    }
}
