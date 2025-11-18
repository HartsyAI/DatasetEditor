using Xunit;
using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using HartsysDatasetEditor.Api.Endpoints;
using HartsysDatasetEditor.Contracts.Items;
using HartsysDatasetEditor.Core.Interfaces;
using HartsysDatasetEditor.Core.Models;
using Moq;

namespace HartsysDatasetEditor.Tests.Api;

public class ItemEditEndpointsTests
{
    private readonly Mock<IDatasetItemRepository> _mockRepository;

    public ItemEditEndpointsTests()
    {
        _mockRepository = new Mock<IDatasetItemRepository>();
    }

    [Fact]
    public async Task UpdateItem_WithValidItem_ReturnsOk()
    {
        // Arrange
        Guid itemId = Guid.NewGuid();
        ImageItem item = new()
        {
            Id = itemId.ToString(),
            Title = "Original Title",
            Description = "Original Description",
            Tags = new List<string> { "old-tag" }
        };

        _mockRepository.Setup(r => r.GetItem(itemId)).Returns(item);
        _mockRepository.Setup(r => r.UpdateItem(It.IsAny<IDatasetItem>()));

        UpdateItemRequest request = new()
        {
            ItemId = itemId,
            Title = "Updated Title",
            Description = "Updated Description",
            Tags = new List<string> { "new-tag" }
        };

        // Act
        IResult result = await ItemEditEndpoints.UpdateItem(itemId, request, _mockRepository.Object);

        // Assert
        result.Should().BeOfType<Ok<Contracts.Datasets.DatasetItemDto>>();
        _mockRepository.Verify(r => r.UpdateItem(It.Is<ImageItem>(i => 
            i.Title == "Updated Title" &&
            i.Description == "Updated Description" &&
            i.Tags.Contains("new-tag")
        )), Times.Once);
    }

    [Fact]
    public async Task UpdateItem_WithNonExistentItem_ReturnsNotFound()
    {
        // Arrange
        Guid itemId = Guid.NewGuid();
        _mockRepository.Setup(r => r.GetItem(itemId)).Returns((IDatasetItem?)null);

        UpdateItemRequest request = new()
        {
            ItemId = itemId,
            Title = "Updated Title"
        };

        // Act
        IResult result = await ItemEditEndpoints.UpdateItem(itemId, request, _mockRepository.Object);

        // Assert
        result.Should().BeOfType<NotFound<object>>();
    }

    [Fact]
    public async Task UpdateItem_WithPartialUpdate_UpdatesOnlyProvidedFields()
    {
        // Arrange
        Guid itemId = Guid.NewGuid();
        ImageItem item = new()
        {
            Id = itemId.ToString(),
            Title = "Original Title",
            Description = "Original Description",
            Tags = new List<string> { "tag1" }
        };

        _mockRepository.Setup(r => r.GetItem(itemId)).Returns(item);
        _mockRepository.Setup(r => r.UpdateItem(It.IsAny<IDatasetItem>()));

        UpdateItemRequest request = new()
        {
            ItemId = itemId,
            Title = "Updated Title"
            // Description and Tags not provided
        };

        // Act
        IResult result = await ItemEditEndpoints.UpdateItem(itemId, request, _mockRepository.Object);

        // Assert
        result.Should().BeOfType<Ok<Contracts.Datasets.DatasetItemDto>>();
        _mockRepository.Verify(r => r.UpdateItem(It.Is<ImageItem>(i =>
            i.Title == "Updated Title" &&
            i.Description == "Original Description" &&
            i.Tags.Contains("tag1")
        )), Times.Once);
    }

    [Fact]
    public async Task UpdateItem_UpdatesFavoriteFlag()
    {
        // Arrange
        Guid itemId = Guid.NewGuid();
        ImageItem item = new()
        {
            Id = itemId.ToString(),
            Title = "Test",
            IsFavorite = false
        };

        _mockRepository.Setup(r => r.GetItem(itemId)).Returns(item);
        _mockRepository.Setup(r => r.UpdateItem(It.IsAny<IDatasetItem>()));

        UpdateItemRequest request = new()
        {
            ItemId = itemId,
            IsFavorite = true
        };

        // Act
        await ItemEditEndpoints.UpdateItem(itemId, request, _mockRepository.Object);

        // Assert
        _mockRepository.Verify(r => r.UpdateItem(It.Is<ImageItem>(i => i.IsFavorite == true)), Times.Once);
    }

    [Fact]
    public async Task BulkUpdateItems_WithMultipleItems_UpdatesAll()
    {
        // Arrange
        Guid itemId1 = Guid.NewGuid();
        Guid itemId2 = Guid.NewGuid();

        ImageItem item1 = new()
        {
            Id = itemId1.ToString(),
            Tags = new List<string> { "old-tag" },
            IsFavorite = false
        };

        ImageItem item2 = new()
        {
            Id = itemId2.ToString(),
            Tags = new List<string> { "old-tag" },
            IsFavorite = false
        };

        _mockRepository.Setup(r => r.GetItem(itemId1)).Returns(item1);
        _mockRepository.Setup(r => r.GetItem(itemId2)).Returns(item2);
        _mockRepository.Setup(r => r.BulkUpdateItems(It.IsAny<IEnumerable<IDatasetItem>>()));

        BulkUpdateItemsRequest request = new()
        {
            ItemIds = new List<Guid> { itemId1, itemId2 },
            TagsToAdd = new List<string> { "new-tag" },
            SetFavorite = true
        };

        // Act
        IResult result = await ItemEditEndpoints.BulkUpdateItems(request, _mockRepository.Object);

        // Assert
        result.Should().BeOfType<Ok<object>>();
        _mockRepository.Verify(r => r.BulkUpdateItems(It.Is<IEnumerable<IDatasetItem>>(items =>
            items.Count() == 2 &&
            items.All(i => ((ImageItem)i).Tags.Contains("new-tag")) &&
            items.All(i => ((ImageItem)i).IsFavorite == true)
        )), Times.Once);
    }

    [Fact]
    public async Task BulkUpdateItems_AddsTagsWithoutDuplicates()
    {
        // Arrange
        Guid itemId = Guid.NewGuid();
        ImageItem item = new()
        {
            Id = itemId.ToString(),
            Tags = new List<string> { "existing-tag", "another-tag" }
        };

        _mockRepository.Setup(r => r.GetItem(itemId)).Returns(item);
        _mockRepository.Setup(r => r.BulkUpdateItems(It.IsAny<IEnumerable<IDatasetItem>>()));

        BulkUpdateItemsRequest request = new()
        {
            ItemIds = new List<Guid> { itemId },
            TagsToAdd = new List<string> { "existing-tag", "new-tag" }
        };

        // Act
        await ItemEditEndpoints.BulkUpdateItems(request, _mockRepository.Object);

        // Assert
        _mockRepository.Verify(r => r.BulkUpdateItems(It.Is<IEnumerable<IDatasetItem>>(items =>
            items.First() is ImageItem img &&
            img.Tags.Count(t => t == "existing-tag") == 1 &&
            img.Tags.Contains("new-tag")
        )), Times.Once);
    }

    [Fact]
    public async Task BulkUpdateItems_RemovesTags()
    {
        // Arrange
        Guid itemId = Guid.NewGuid();
        ImageItem item = new()
        {
            Id = itemId.ToString(),
            Tags = new List<string> { "tag1", "tag2", "tag3" }
        };

        _mockRepository.Setup(r => r.GetItem(itemId)).Returns(item);
        _mockRepository.Setup(r => r.BulkUpdateItems(It.IsAny<IEnumerable<IDatasetItem>>()));

        BulkUpdateItemsRequest request = new()
        {
            ItemIds = new List<Guid> { itemId },
            TagsToRemove = new List<string> { "tag2" }
        };

        // Act
        await ItemEditEndpoints.BulkUpdateItems(request, _mockRepository.Object);

        // Assert
        _mockRepository.Verify(r => r.BulkUpdateItems(It.Is<IEnumerable<IDatasetItem>>(items =>
            items.First() is ImageItem img &&
            img.Tags.Contains("tag1") &&
            !img.Tags.Contains("tag2") &&
            img.Tags.Contains("tag3")
        )), Times.Once);
    }

    [Fact]
    public async Task BulkUpdateItems_WithNoItemIds_ReturnsBadRequest()
    {
        // Arrange
        BulkUpdateItemsRequest request = new()
        {
            ItemIds = new List<Guid>(),
            TagsToAdd = new List<string> { "new-tag" }
        };

        // Act
        IResult result = await ItemEditEndpoints.BulkUpdateItems(request, _mockRepository.Object);

        // Assert
        result.Should().BeOfType<BadRequest<object>>();
    }

    [Fact]
    public async Task BulkUpdateItems_SkipsNonExistentItems()
    {
        // Arrange
        Guid existingId = Guid.NewGuid();
        Guid nonExistentId = Guid.NewGuid();

        ImageItem existingItem = new()
        {
            Id = existingId.ToString(),
            Tags = new List<string>()
        };

        _mockRepository.Setup(r => r.GetItem(existingId)).Returns(existingItem);
        _mockRepository.Setup(r => r.GetItem(nonExistentId)).Returns((IDatasetItem?)null);
        _mockRepository.Setup(r => r.BulkUpdateItems(It.IsAny<IEnumerable<IDatasetItem>>()));

        BulkUpdateItemsRequest request = new()
        {
            ItemIds = new List<Guid> { existingId, nonExistentId },
            TagsToAdd = new List<string> { "new-tag" }
        };

        // Act
        await ItemEditEndpoints.BulkUpdateItems(request, _mockRepository.Object);

        // Assert
        _mockRepository.Verify(r => r.BulkUpdateItems(It.Is<IEnumerable<IDatasetItem>>(items =>
            items.Count() == 1  // Only existing item updated
        )), Times.Once);
    }

    [Fact]
    public async Task BulkUpdateItems_AddsMetadata()
    {
        // Arrange
        Guid itemId = Guid.NewGuid();
        ImageItem item = new()
        {
            Id = itemId.ToString(),
            Metadata = new Dictionary<string, string>()
        };

        _mockRepository.Setup(r => r.GetItem(itemId)).Returns(item);
        _mockRepository.Setup(r => r.BulkUpdateItems(It.IsAny<IEnumerable<IDatasetItem>>()));

        BulkUpdateItemsRequest request = new()
        {
            ItemIds = new List<Guid> { itemId },
            MetadataToAdd = new Dictionary<string, string>
            {
                ["custom_field"] = "custom_value"
            }
        };

        // Act
        await ItemEditEndpoints.BulkUpdateItems(request, _mockRepository.Object);

        // Assert
        _mockRepository.Verify(r => r.BulkUpdateItems(It.Is<IEnumerable<IDatasetItem>>(items =>
            items.First() is ImageItem img &&
            img.Metadata.ContainsKey("custom_field") &&
            img.Metadata["custom_field"] == "custom_value"
        )), Times.Once);
    }
}
