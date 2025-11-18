using HartsysDatasetEditor.Api.Models;
using HartsysDatasetEditor.Contracts.Datasets;

namespace HartsysDatasetEditor.Api.Services.Dtos;

internal static class DatasetMappings
{
    public static DatasetSummaryDto ToSummaryDto(this DatasetEntity entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        Description = entity.Description,
        Status = entity.Status,
        TotalItems = entity.TotalItems,
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt,
        SourceType = entity.SourceType,
        SourceUri = entity.SourceUri,
        IsStreaming = entity.IsStreaming,
    };

    public static DatasetDetailDto ToDetailDto(this DatasetEntity entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        Description = entity.Description,
        Status = entity.Status,
        TotalItems = entity.TotalItems,
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt,
        SourceFileName = entity.SourceFileName,
        SourceType = entity.SourceType,
        SourceUri = entity.SourceUri,
        IsStreaming = entity.IsStreaming,
    };
}
