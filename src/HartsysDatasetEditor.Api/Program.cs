using HartsysDatasetEditor.Api.Extensions;
using HartsysDatasetEditor.Api.Models;
using HartsysDatasetEditor.Api.Services;
using HartsysDatasetEditor.Api.Services.Dtos;
using HartsysDatasetEditor.Contracts.Common;
using HartsysDatasetEditor.Contracts.Datasets;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddDatasetServices();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

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

app.Run();
