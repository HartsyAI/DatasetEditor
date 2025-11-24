using Microsoft.AspNetCore.Mvc;
using HartsysDatasetEditor.Api.Extensions;
using HartsysDatasetEditor.Api.Models;
using HartsysDatasetEditor.Api.Services;
using HartsysDatasetEditor.Api.Services.Dtos;
using HartsysDatasetEditor.Contracts.Common;
using HartsysDatasetEditor.Contracts.Datasets;

namespace HartsysDatasetEditor.Api.Endpoints;

/// <summary>Dataset management endpoints</summary>
internal static class DatasetEndpoints
{
    /// <summary>Maps all dataset endpoints</summary>
    internal static void MapDatasetEndpoints(this WebApplication app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/datasets").WithTags("Datasets");

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
        IDatasetItemRepository repository,
        CancellationToken cancellationToken)
    {
        int size = pageSize.GetValueOrDefault(100);

        (IReadOnlyList<DatasetItemDto>? items, string? nextCursor) = await repository.GetPageAsync(
            datasetId,
            null,
            cursor,
            size,
            cancellationToken);

        PageResponse<DatasetItemDto> response = new()
        {
            Items = items,
            NextCursor = nextCursor,
            TotalCount = null // Unknown in current implementation
        };

        return Results.Ok(response);
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
}
