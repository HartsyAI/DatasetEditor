using HartsysDatasetEditor.Api.Extensions;
using HartsysDatasetEditor.Api.Models;
using HartsysDatasetEditor.Api.Services;
using HartsysDatasetEditor.Api.Services.Dtos;
using HartsysDatasetEditor.Contracts.Common;
using HartsysDatasetEditor.Contracts.Datasets;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDatasetServices();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Hartsy Dataset Editor API",
        Version = "v1",
        Description = "API-first surface for billion-scale dataset management"
    });
});

var corsPolicyName = "DatasetEditorClient";
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddPolicy(corsPolicyName, policy =>
    {
        if (allowedOrigins.Length == 0)
        {
            policy.AllowAnyOrigin();
        }
        else
        {
            policy.WithOrigins(allowedOrigins);
        }

        policy
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseCors(corsPolicyName);

app.MapPost("/api/datasets", async (
    CreateDatasetRequest request,
    IDatasetRepository repository,
    IDatasetIngestionService ingestionService,
    CancellationToken cancellationToken) =>
{
    var entity = new DatasetEntity
    {
        Id = Guid.NewGuid(),
        Name = request.Name,
        Description = request.Description,
        Status = IngestionStatusDto.Pending,
    };

    await repository.CreateAsync(entity, cancellationToken);
    await ingestionService.StartIngestionAsync(entity.Id, uploadLocation: null, cancellationToken);

    return Results.Created($"/api/datasets/{entity.Id}", entity.ToDetailDto());
})
.WithName("CreateDataset")
.WithOpenApi();

app.MapGet("/api/datasets", async (IDatasetRepository repository, CancellationToken cancellationToken) =>
{
    var datasets = await repository.ListAsync(cancellationToken);
    var summaries = datasets.Select(d => d.ToSummaryDto()).ToList();
    return Results.Ok(summaries);
})
.WithName("ListDatasets")
.WithOpenApi();

app.MapGet("/api/datasets/{datasetId:guid}", async (
    Guid datasetId,
    IDatasetRepository repository,
    CancellationToken cancellationToken) =>
{
    var dataset = await repository.GetAsync(datasetId, cancellationToken);
    if (dataset is null)
    {
        return Results.NotFound();
    }

    return Results.Ok(dataset.ToDetailDto());
})
.WithName("GetDataset")
.WithOpenApi();

app.MapPost("/api/datasets/{datasetId:guid}/upload", async (
    Guid datasetId,
    IFormFile file,
    IDatasetRepository repository,
    IDatasetIngestionService ingestionService,
    CancellationToken cancellationToken) =>
{
    var dataset = await repository.GetAsync(datasetId, cancellationToken);
    if (dataset is null)
    {
        return Results.NotFound();
    }

    if (file is null || file.Length == 0)
    {
        return Results.BadRequest("No file uploaded or file is empty.");
    }

    var tempFilePath = Path.Combine(Path.GetTempPath(), $"dataset-{datasetId}-{Guid.NewGuid()}{Path.GetExtension(file.FileName)}");

    await using (var stream = File.Create(tempFilePath))
    {
        await file.CopyToAsync(stream, cancellationToken);
    }

    dataset.SourceFileName = file.FileName;
    await repository.UpdateAsync(dataset, cancellationToken);
    await ingestionService.StartIngestionAsync(datasetId, tempFilePath, cancellationToken);

    return Results.Accepted($"/api/datasets/{datasetId}", new { datasetId, fileName = file.FileName });
})
.Accepts<IFormFile>("multipart/form-data")
.WithName("UploadDatasetFile")
.WithOpenApi();

app.MapGet("/api/datasets/{datasetId:guid}/items", async (
    Guid datasetId,
    int? pageSize,
    string? cursor,
    [AsParameters] FilterRequest? filter,
    IDatasetItemRepository repository,
    CancellationToken cancellationToken) =>
{
    var size = pageSize.GetValueOrDefault(100);
    var (items, nextCursor) = await repository.GetPageAsync(
        datasetId,
        filter,
        cursor,
        size,
        cancellationToken);

    var response = new PageResponse<DatasetItemDto>
    {
        Items = items,
        NextCursor = nextCursor,
        TotalCount = null // Unknown in in-memory stub
    };

    return Results.Ok(response);
})
.WithName("ListDatasetItems")
.WithOpenApi();

app.MapFallbackToFile("index.html");

app.Run();
