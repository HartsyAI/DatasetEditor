using DatasetStudio.APIBackend.DataAccess.PostgreSQL.Repositories;
using DatasetStudio.DTO.Datasets;

namespace DatasetStudio.APIBackend.Services.DatasetManagement.Dtos;

/// <summary>
/// The one place a <see cref="DatasetRecord"/> becomes a wire DTO. The fields the summary and the
/// full DTO share are copied once, in <see cref="CopySummaryFields"/>; there used to be two
/// hand-written mappers listing them twice (audit K-5).
/// </summary>
internal static class DatasetMappings
{
    /// <summary>The library's projection of a dataset.</summary>
    public static DatasetSummaryDto ToSummaryDto(this DatasetRecord dataset) =>
        CopySummaryFields(new DatasetSummaryDto(), dataset);

    /// <summary>The single-dataset shape. <see cref="DatasetDto.CanShare"/> is left false; the
    /// GET endpoint sets it for the caller.</summary>
    public static DatasetDto ToDto(this DatasetRecord dataset) =>
        (DatasetDto)CopySummaryFields(
            new DatasetDto
            {
                SourceFileName = dataset.SourceFileName,
                ErrorMessage = dataset.ErrorMessage,
            },
            dataset);

    // `with` clones through the record's virtual clone method, so a DatasetDto passed in comes
    // back as a DatasetDto with its own fields intact.
    private static DatasetSummaryDto CopySummaryFields(DatasetSummaryDto target, DatasetRecord dataset) => target with
    {
        Id = dataset.Id,
        Name = dataset.Name,
        Description = dataset.Description,
        Status = dataset.Status,
        TotalItems = dataset.TotalItems,
        CreatedAt = dataset.CreatedAt,
        UpdatedAt = dataset.UpdatedAt,
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
}
