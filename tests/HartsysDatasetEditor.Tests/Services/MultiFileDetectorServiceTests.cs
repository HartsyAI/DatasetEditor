using Xunit;
using FluentAssertions;
using HartsysDatasetEditor.Core.Services;
using HartsysDatasetEditor.Core.Models;

namespace HartsysDatasetEditor.Tests.Services;

public class MultiFileDetectorServiceTests
{
    private readonly MultiFileDetectorService _service;

    public MultiFileDetectorServiceTests()
    {
        _service = new MultiFileDetectorService();
    }

    [Fact]
    public void AnalyzeFiles_WithSingleFile_DetectsPrimaryFile()
    {
        // Arrange
        Dictionary<string, string> files = new()
        {
            ["photos.csv"] = "photo_id,photo_image_url,photo_description\n1,http://example.com/1.jpg,Test"
        };

        // Act
        DatasetFileCollection result = _service.AnalyzeFiles(files);

        // Assert
        result.PrimaryFileName.Should().Be("photos.csv");
        result.EnrichmentFiles.Should().BeEmpty();
    }

    [Fact]
    public void AnalyzeFiles_WithMultipleFiles_DetectsPrimaryAndEnrichments()
    {
        // Arrange
        Dictionary<string, string> files = new()
        {
            ["photos.csv000"] = "photo_id,photo_image_url,photo_description\n1,http://example.com/1.jpg,Test",
            ["colors.csv000"] = "photo_id,hex,red,green,blue\n1,#FF5733,255,87,51",
            ["tags.csv000"] = "photo_id,tag\n1,nature"
        };

        // Act
        DatasetFileCollection result = _service.AnalyzeFiles(files);

        // Assert
        result.PrimaryFileName.Should().Be("photos.csv000");
        result.EnrichmentFiles.Should().HaveCount(2);
        result.EnrichmentFiles.Should().Contain(e => e.FileName == "colors.csv000");
        result.EnrichmentFiles.Should().Contain(e => e.FileName == "tags.csv000");
    }

    [Fact]
    public void HasImageUrlColumn_WithValidImageUrl_ReturnsTrue()
    {
        // Arrange
        string content = "photo_id,photo_image_url,description\n1,http://example.com/1.jpg,Test";

        // Act
        bool result = _service.HasImageUrlColumn(content);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void HasImageUrlColumn_WithoutImageUrl_ReturnsFalse()
    {
        // Arrange
        string content = "photo_id,description,tags\n1,Test,nature";

        // Act
        bool result = _service.HasImageUrlColumn(content);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void AnalyzeEnrichmentFile_WithColorFile_DetectsColorEnrichment()
    {
        // Arrange
        string content = "photo_id,hex,red,green,blue,keyword\n1,#FF5733,255,87,51,warm";

        // Act
        EnrichmentFile result = _service.AnalyzeEnrichmentFile("colors.csv", content);

        // Assert
        result.Info.EnrichmentType.Should().Be("colors");
        result.Info.ForeignKeyColumn.Should().Be("photo_id");
        result.Info.ColumnsToMerge.Should().Contain("hex");
        result.Info.RecordCount.Should().Be(1);
    }

    [Fact]
    public void AnalyzeEnrichmentFile_WithTagFile_DetectsTagEnrichment()
    {
        // Arrange
        string content = "photo_id,tag,confidence\n1,nature,0.95\n1,landscape,0.87";

        // Act
        EnrichmentFile result = _service.AnalyzeEnrichmentFile("tags.csv", content);

        // Assert
        result.Info.EnrichmentType.Should().Be("tags");
        result.Info.ForeignKeyColumn.Should().Be("photo_id");
        result.Info.ColumnsToMerge.Should().Contain("tag");
        result.Info.RecordCount.Should().Be(2);
    }

    [Fact]
    public void AnalyzeEnrichmentFile_WithCollectionFile_DetectsCollectionEnrichment()
    {
        // Arrange
        string content = "photo_id,collection_id,collection_title\n1,123,Nature Photos";

        // Act
        EnrichmentFile result = _service.AnalyzeEnrichmentFile("collections.csv", content);

        // Assert
        result.Info.EnrichmentType.Should().Be("collections");
        result.Info.ForeignKeyColumn.Should().Be("photo_id");
        result.Info.ColumnsToMerge.Should().Contain("collection_title");
        result.Info.RecordCount.Should().Be(1);
    }

    [Fact]
    public void DetectForeignKeyColumn_WithPhotoId_ReturnsPhotoId()
    {
        // Arrange
        string[] headers = { "photo_id", "hex", "red", "green", "blue" };

        // Act
        string result = _service.DetectForeignKeyColumn(headers);

        // Assert
        result.Should().Be("photo_id");
    }

    [Fact]
    public void DetectForeignKeyColumn_WithImageId_ReturnsImageId()
    {
        // Arrange
        string[] headers = { "image_id", "tag", "confidence" };

        // Act
        string result = _service.DetectForeignKeyColumn(headers);

        // Assert
        result.Should().Be("image_id");
    }

    [Fact]
    public void DetectForeignKeyColumn_WithNoMatch_ReturnsFirstColumn()
    {
        // Arrange
        string[] headers = { "custom_id", "data1", "data2" };

        // Act
        string result = _service.DetectForeignKeyColumn(headers);

        // Assert
        result.Should().Be("custom_id");
    }

    [Fact]
    public void AnalyzeFiles_WithNoFiles_ReturnsEmptyCollection()
    {
        // Arrange
        Dictionary<string, string> files = new();

        // Act
        DatasetFileCollection result = _service.AnalyzeFiles(files);

        // Assert
        result.PrimaryFileName.Should().BeEmpty();
        result.EnrichmentFiles.Should().BeEmpty();
    }

    [Fact]
    public void AnalyzeFiles_CalculatesTotalSize()
    {
        // Arrange
        Dictionary<string, string> files = new()
        {
            ["photos.csv"] = "photo_id,photo_image_url\n1,http://example.com/1.jpg",
            ["colors.csv"] = "photo_id,hex\n1,#FF5733"
        };

        // Act
        DatasetFileCollection result = _service.AnalyzeFiles(files);

        // Assert
        result.TotalSizeBytes.Should().BeGreaterThan(0);
    }
}
