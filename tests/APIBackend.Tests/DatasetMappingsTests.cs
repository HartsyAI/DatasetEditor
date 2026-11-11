using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using DatasetStudio.APIBackend.DataAccess.PostgreSQL.Repositories;
using DatasetStudio.APIBackend.Services.DatasetManagement.Dtos;
using DatasetStudio.DTO.Datasets;
using FluentAssertions;
using Xunit;

namespace DatasetStudio.Tests.APIBackend
{
    /// <summary>
    /// The dataset wire contract after the DTO consolidation (audit K-5): DatasetDetailDto became
    /// DatasetDto, extending DatasetSummaryDto, and the API's internal record became
    /// DatasetRecord. The JSON the client reads must keep the same property names and values.
    /// </summary>
    public sealed class DatasetMappingsTests
    {
        private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

        // The summary's JSON properties before the consolidation, minus "format" (audit D-5).
        private static readonly string[] SummaryProperties =
        {
            "id", "name", "description", "status", "totalItems", "createdAt", "updatedAt", "modality",
            "sourceType", "sourceUri", "isStreaming", "huggingFaceRepository", "huggingFaceConfig",
            "huggingFaceSplit", "s3StorageProfileName", "s3Bucket", "s3Prefix", "isPublic", "createdByUserId",
        };

        // DatasetDetailDto's JSON properties, minus "format" and "totalSizeBytes" (audit D-5).
        private static readonly string[] DatasetProperties =
            SummaryProperties.Concat(new[] { "sourceFileName", "errorMessage", "canShare" }).ToArray();

        private static DatasetRecord FullRecord() => new()
        {
            Id = Guid.NewGuid(),
            Name = "name",
            Description = "description",
            Status = IngestionStatusDto.Failed,
            TotalItems = 42,
            CreatedAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc),
            SourceFileName = "file.csv",
            SourceType = DatasetSourceType.ExternalS3Streaming,
            SourceUri = "s3://bucket/prefix",
            IsStreaming = true,
            HuggingFaceRepository = "org/repo",
            HuggingFaceConfig = "default",
            HuggingFaceSplit = "train",
            S3StorageProfileName = "profile",
            S3Bucket = "bucket",
            S3Prefix = "prefix/",
            ErrorMessage = "boom",
            Modality = "Image",
            StoragePath = "/srv/secret/folder",
            IsPublic = true,
            CreatedByUserId = Guid.NewGuid(),
        };

        private static List<string> PropertyNames(JsonElement json) =>
            json.EnumerateObject().Select(p => p.Name).ToList();

        [Fact]
        public void ToDto_CopiesEveryFieldAndSerialisesTheDetailShape()
        {
            DatasetRecord record = FullRecord();

            DatasetDto dto = record.ToDto() with { CanShare = true };
            JsonElement json = JsonSerializer.SerializeToElement(dto, Web);

            PropertyNames(json).Should().BeEquivalentTo(DatasetProperties);
            json.GetProperty("id").GetGuid().Should().Be(record.Id);
            json.GetProperty("name").GetString().Should().Be("name");
            json.GetProperty("description").GetString().Should().Be("description");
            json.GetProperty("status").GetInt32().Should().Be((int)IngestionStatusDto.Failed);
            json.GetProperty("totalItems").GetInt64().Should().Be(42);
            json.GetProperty("createdAt").GetDateTime().Should().Be(record.CreatedAt);
            json.GetProperty("updatedAt").GetDateTime().Should().Be(record.UpdatedAt);
            json.GetProperty("sourceFileName").GetString().Should().Be("file.csv");
            json.GetProperty("sourceType").GetInt32().Should().Be((int)DatasetSourceType.ExternalS3Streaming);
            json.GetProperty("sourceUri").GetString().Should().Be("s3://bucket/prefix");
            json.GetProperty("isStreaming").GetBoolean().Should().BeTrue();
            json.GetProperty("huggingFaceRepository").GetString().Should().Be("org/repo");
            json.GetProperty("huggingFaceConfig").GetString().Should().Be("default");
            json.GetProperty("huggingFaceSplit").GetString().Should().Be("train");
            json.GetProperty("s3StorageProfileName").GetString().Should().Be("profile");
            json.GetProperty("s3Bucket").GetString().Should().Be("bucket");
            json.GetProperty("s3Prefix").GetString().Should().Be("prefix/");
            json.GetProperty("errorMessage").GetString().Should().Be("boom");
            json.GetProperty("modality").GetString().Should().Be("Image");
            json.GetProperty("isPublic").GetBoolean().Should().BeTrue();
            json.GetProperty("createdByUserId").GetGuid().Should().Be(record.CreatedByUserId!.Value);
            json.GetProperty("canShare").GetBoolean().Should().BeTrue();
        }

        [Fact]
        public void ToSummaryDto_SerialisesOnlyTheLibraryFields()
        {
            DatasetRecord record = FullRecord();

            List<DatasetSummaryDto> page = new() { record.ToSummaryDto() };
            JsonElement json = JsonSerializer.SerializeToElement(page, Web)[0];

            PropertyNames(json).Should().BeEquivalentTo(SummaryProperties);
            json.GetProperty("id").GetGuid().Should().Be(record.Id);
            json.GetProperty("s3Prefix").GetString().Should().Be("prefix/");
        }

        [Fact]
        public void DatasetDto_RoundTripsThroughTheClientsDeserialiser()
        {
            DatasetDto original = FullRecord().ToDto() with { CanShare = true };

            DatasetDto? read = JsonSerializer.Deserialize<DatasetDto>(JsonSerializer.Serialize(original, Web), Web);

            read.Should().Be(original);
        }

        [Fact]
        public void SummaryAndDto_ShareOneSetOfDefaults()
        {
            DatasetSummaryDto summary = new();
            DatasetDto dto = new();

            dto.Modality.Should().Be(summary.Modality).And.Be("Image");
            dto.IsPublic.Should().Be(summary.IsPublic).And.BeFalse();
            dto.SourceType.Should().Be(summary.SourceType);
            dto.Status.Should().Be(summary.Status);
            new DatasetRecord().Modality.Should().Be(dto.Modality);
        }
    }
}
