using Xunit;
using FluentAssertions;
using HartsysDatasetEditor.Core.Services;
using HartsysDatasetEditor.Core.Models;
using HartsysDatasetEditor.Core.Interfaces;

namespace HartsysDatasetEditor.Tests.Services;

public class EnrichmentMergerServiceTests
{
    private readonly EnrichmentMergerService _service;

    public EnrichmentMergerServiceTests()
    {
        _service = new EnrichmentMergerService();
    }

    [Fact]
    public async Task MergeEnrichmentsAsync_WithColorFile_MergesColorData()
    {
        // Arrange
        List<IDatasetItem> items = new()
        {
            new ImageItem
            {
                Id = "1",
                Title = "Test Image",
                DominantColors = new()
            }
        };

        EnrichmentFile colorFile = new()
        {
            FileName = "colors.csv",
            Content = "photo_id,hex\n1,#FF5733",
            Info = new EnrichmentFileInfo
            {
                EnrichmentType = "colors",
                ForeignKeyColumn = "photo_id",
                ColumnsToMerge = new List<string> { "hex" }
            }
        };

        List<EnrichmentFile> enrichments = new() { colorFile };

        // Act
        List<IDatasetItem> result = await _service.MergeEnrichmentsAsync(items, enrichments);

        // Assert
        ImageItem item = (ImageItem)result[0];
        item.AverageColor.Should().Be("#FF5733");
        item.DominantColors.Should().Contain("#FF5733");
    }

    [Fact]
    public async Task MergeEnrichmentsAsync_WithTagFile_MergesTagData()
    {
        // Arrange
        List<IDatasetItem> items = new()
        {
            new ImageItem
            {
                Id = "1",
                Title = "Test Image",
                Tags = new()
            }
        };

        EnrichmentFile tagFile = new()
        {
            FileName = "tags.csv",
            Content = "photo_id,tag\n1,nature\n1,landscape",
            Info = new EnrichmentFileInfo
            {
                EnrichmentType = "tags",
                ForeignKeyColumn = "photo_id",
                ColumnsToMerge = new List<string> { "tag" }
            }
        };

        List<EnrichmentFile> enrichments = new() { tagFile };

        // Act
        List<IDatasetItem> result = await _service.MergeEnrichmentsAsync(items, enrichments);

        // Assert
        ImageItem item = (ImageItem)result[0];
        item.Tags.Should().Contain("nature");
        item.Tags.Should().Contain("landscape");
    }

    [Fact]
    public async Task MergeEnrichmentsAsync_WithCollectionFile_MergesCollectionData()
    {
        // Arrange
        List<IDatasetItem> items = new()
        {
            new ImageItem
            {
                Id = "1",
                Title = "Test Image",
                Tags = new(),
                Metadata = new()
            }
        };

        EnrichmentFile collectionFile = new()
        {
            FileName = "collections.csv",
            Content = "photo_id,collection_title\n1,Nature Collection",
            Info = new EnrichmentFileInfo
            {
                EnrichmentType = "collections",
                ForeignKeyColumn = "photo_id",
                ColumnsToMerge = new List<string> { "collection_title" }
            }
        };

        List<EnrichmentFile> enrichments = new() { collectionFile };

        // Act
        List<IDatasetItem> result = await _service.MergeEnrichmentsAsync(items, enrichments);

        // Assert
        ImageItem item = (ImageItem)result[0];
        item.Tags.Should().Contain("Nature Collection");
        item.Metadata.Should().ContainKey("collection_collection_title");
    }

    [Fact]
    public async Task MergeEnrichmentsAsync_WithMultipleEnrichments_MergesAll()
    {
        // Arrange
        List<IDatasetItem> items = new()
        {
            new ImageItem
            {
                Id = "1",
                Title = "Test Image",
                Tags = new(),
                DominantColors = new(),
                Metadata = new()
            }
        };

        EnrichmentFile colorFile = new()
        {
            FileName = "colors.csv",
            Content = "photo_id,hex\n1,#FF5733",
            Info = new EnrichmentFileInfo
            {
                EnrichmentType = "colors",
                ForeignKeyColumn = "photo_id",
                ColumnsToMerge = new List<string> { "hex" }
            }
        };

        EnrichmentFile tagFile = new()
        {
            FileName = "tags.csv",
            Content = "photo_id,tag\n1,nature",
            Info = new EnrichmentFileInfo
            {
                EnrichmentType = "tags",
                ForeignKeyColumn = "photo_id",
                ColumnsToMerge = new List<string> { "tag" }
            }
        };

        List<EnrichmentFile> enrichments = new() { colorFile, tagFile };

        // Act
        List<IDatasetItem> result = await _service.MergeEnrichmentsAsync(items, enrichments);

        // Assert
        ImageItem item = (ImageItem)result[0];
        item.AverageColor.Should().Be("#FF5733");
        item.Tags.Should().Contain("nature");
        item.DominantColors.Should().Contain("#FF5733");
    }

    [Fact]
    public async Task MergeEnrichmentsAsync_WithMissingForeignKey_SkipsItem()
    {
        // Arrange
        List<IDatasetItem> items = new()
        {
            new ImageItem
            {
                Id = "1",
                Title = "Test Image",
                Tags = new()
            }
        };

        EnrichmentFile tagFile = new()
        {
            FileName = "tags.csv",
            Content = "photo_id,tag\n2,nature",  // Different ID
            Info = new EnrichmentFileInfo
            {
                EnrichmentType = "tags",
                ForeignKeyColumn = "photo_id",
                ColumnsToMerge = new List<string> { "tag" }
            }
        };

        List<EnrichmentFile> enrichments = new() { tagFile };

        // Act
        List<IDatasetItem> result = await _service.MergeEnrichmentsAsync(items, enrichments);

        // Assert
        ImageItem item = (ImageItem)result[0];
        item.Tags.Should().BeEmpty();
    }

    [Fact]
    public void MergeColorData_WithHexColor_SetsAverageColor()
    {
        // Arrange
        ImageItem item = new()
        {
            DominantColors = new(),
            Metadata = new()
        };
        Dictionary<string, string> data = new()
        {
            ["hex"] = "#FF5733"
        };

        // Act
        _service.MergeColorData(item, data);

        // Assert
        item.AverageColor.Should().Be("#FF5733");
    }

    [Fact]
    public void MergeTagData_WithMultipleTags_AddsAllTags()
    {
        // Arrange
        ImageItem item = new()
        {
            Tags = new()
        };
        Dictionary<string, string> data = new()
        {
            ["tag"] = "nature, landscape, mountains"
        };

        // Act
        _service.MergeTagData(item, data);

        // Assert
        item.Tags.Should().Contain("nature");
        item.Tags.Should().Contain("landscape");
        item.Tags.Should().Contain("mountains");
    }

    [Fact]
    public void MergeTagData_WithDuplicateTags_DoesNotAddDuplicates()
    {
        // Arrange
        ImageItem item = new()
        {
            Tags = new List<string> { "nature" }
        };
        Dictionary<string, string> data = new()
        {
            ["tag"] = "nature"
        };

        // Act
        _service.MergeTagData(item, data);

        // Assert
        item.Tags.Should().HaveCount(1);
        item.Tags.Should().Contain("nature");
    }

    [Fact]
    public void MergeCollectionData_AddsCollectionAsTag()
    {
        // Arrange
        ImageItem item = new()
        {
            Tags = new(),
            Metadata = new()
        };
        Dictionary<string, string> data = new()
        {
            ["collection_title"] = "Nature Collection"
        };

        // Act
        _service.MergeCollectionData(item, data);

        // Assert
        item.Tags.Should().Contain("Nature Collection");
        item.Metadata.Should().ContainKey("collection_collection_title");
    }

    [Fact]
    public async Task ParseEnrichmentDataAsync_ReturnsCorrectDictionary()
    {
        // Arrange
        EnrichmentFile enrichment = new()
        {
            Content = "photo_id,hex,red,green\n1,#FF5733,255,87\n2,#33FF57,51,255",
            Info = new EnrichmentFileInfo
            {
                ForeignKeyColumn = "photo_id",
                ColumnsToMerge = new List<string> { "hex", "red", "green" }
            }
        };

        // Act
        Dictionary<string, Dictionary<string, string>> result = await _service.ParseEnrichmentDataAsync(enrichment);

        // Assert
        result.Should().HaveCount(2);
        result["1"]["hex"].Should().Be("#FF5733");
        result["1"]["red"].Should().Be("255");
        result["2"]["hex"].Should().Be("#33FF57");
    }
}
