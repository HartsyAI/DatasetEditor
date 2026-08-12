using DatasetStudio.DTO.Datasets;

namespace DatasetStudio.APIBackend.Services.DatasetManagement.Dtos;

internal static class DatasetMappings
{
    public static DatasetSummaryDto ToSummaryDto(this DatasetDto dataset) => new()
    {
        Id = dataset.Id,
        Name = dataset.Name,
        Description = dataset.Description,
        Status = dataset.Status,
        TotalItems = dataset.TotalItems,
        CreatedAt = dataset.CreatedAt,
        UpdatedAt = dataset.UpdatedAt,
        Format = dataset.Format,
        Modality = dataset.Modality,
        SourceType = dataset.SourceType,
        SourceUri = dataset.SourceUri,
        IsStreaming = dataset.IsStreaming,
        HuggingFaceRepository = dataset.HuggingFaceRepository,
        HuggingFaceConfig = dataset.HuggingFaceConfig,
        HuggingFaceSplit = dataset.HuggingFaceSplit,
        S3StorageProfileName = dataset.S3StorageProfileName,
        S3Bucket = dataset.S3Bucket,
        S3Prefix = dataset.S3Prefix,
        IsPublic = dataset.IsPublic,
        CreatedByUserId = dataset.CreatedByUserId,
    };

    public static DatasetDetailDto ToDetailDto(this DatasetDto dataset) => new()
    {
        Id = dataset.Id,
        Name = dataset.Name,
        Description = dataset.Description,
        Status = dataset.Status,
        TotalItems = dataset.TotalItems,
        CreatedAt = dataset.CreatedAt,
        UpdatedAt = dataset.UpdatedAt,
        SourceFileName = dataset.SourceFileName,
        SourceType = dataset.SourceType,
        SourceUri = dataset.SourceUri,
        IsStreaming = dataset.IsStreaming,
        HuggingFaceRepository = dataset.HuggingFaceRepository,
        HuggingFaceConfig = dataset.HuggingFaceConfig,
        HuggingFaceSplit = dataset.HuggingFaceSplit,
        S3StorageProfileName = dataset.S3StorageProfileName,
        S3Bucket = dataset.S3Bucket,
        S3Prefix = dataset.S3Prefix,
        ErrorMessage = dataset.ErrorMessage,
        Format = dataset.Format,
        Modality = dataset.Modality,
        TotalSizeBytes = dataset.TotalSizeBytes,
        IsPublic = dataset.IsPublic,
        CreatedByUserId = dataset.CreatedByUserId,
    };
}
