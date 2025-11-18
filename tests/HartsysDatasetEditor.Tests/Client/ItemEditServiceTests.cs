using Xunit;
using FluentAssertions;
using Moq;
using Moq.Protected;
using System.Net;
using System.Net.Http.Json;
using HartsysDatasetEditor.Client.Services;
using HartsysDatasetEditor.Client.Services.StateManagement;
using HartsysDatasetEditor.Core.Models;

namespace HartsysDatasetEditor.Tests.Client;

public class ItemEditServiceTests
{
    private readonly Mock<HttpMessageHandler> _mockHttpHandler;
    private readonly HttpClient _httpClient;
    private readonly Mock<DatasetState> _mockDatasetState;
    private readonly ItemEditService _service;

    public ItemEditServiceTests()
    {
        _mockHttpHandler = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_mockHttpHandler.Object)
        {
            BaseAddress = new Uri("https://localhost:7085")
        };
        _mockDatasetState = new Mock<DatasetState>();
        _service = new ItemEditService(_httpClient, _mockDatasetState.Object);
    }

    [Fact]
    public async Task UpdateItemAsync_WithSuccessResponse_UpdatesLocalItem()
    {
        // Arrange
        ImageItem item = new()
        {
            Id = "1",
            Title = "Old Title",
            Description = "Old Description"
        };

        _mockHttpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = JsonContent.Create(new { })
            });

        // Act
        bool result = await _service.UpdateItemAsync(item, title: "New Title");

        // Assert
        result.Should().BeTrue();
        item.Title.Should().Be("New Title");
        _mockDatasetState.Verify(s => s.UpdateItem(item), Times.Once);
    }

    [Fact]
    public async Task UpdateItemAsync_WithFailureResponse_ReturnsFalse()
    {
        // Arrange
        ImageItem item = new()
        {
            Id = "1",
            Title = "Old Title"
        };

        _mockHttpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.NotFound
            });

        // Act
        bool result = await _service.UpdateItemAsync(item, title: "New Title");

        // Assert
        result.Should().BeFalse();
        _mockDatasetState.Verify(s => s.UpdateItem(It.IsAny<ImageItem>()), Times.Never);
    }

    [Fact]
    public async Task UpdateItemAsync_ClearsDirtyState()
    {
        // Arrange
        ImageItem item = new()
        {
            Id = "1",
            Title = "Old Title"
        };

        _service.DirtyItemIds.Add("1");

        _mockHttpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = JsonContent.Create(new { })
            });

        // Act
        await _service.UpdateItemAsync(item, title: "New Title");

        // Assert
        _service.DirtyItemIds.Should().NotContain("1");
    }

    [Fact]
    public void MarkDirty_AddsItemToDirtySet()
    {
        // Arrange
        string itemId = "1";

        // Act
        _service.MarkDirty(itemId);

        // Assert
        _service.DirtyItemIds.Should().Contain(itemId);
    }

    [Fact]
    public void MarkDirty_RaisesOnDirtyStateChanged()
    {
        // Arrange
        bool eventRaised = false;
        _service.OnDirtyStateChanged += () => eventRaised = true;

        // Act
        _service.MarkDirty("1");

        // Assert
        eventRaised.Should().BeTrue();
    }

    [Fact]
    public async Task AddTagAsync_WithNewTag_CallsUpdateItem()
    {
        // Arrange
        ImageItem item = new()
        {
            Id = "1",
            Tags = new List<string> { "existing-tag" }
        };

        _mockHttpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = JsonContent.Create(new { })
            });

        // Act
        bool result = await _service.AddTagAsync(item, "new-tag");

        // Assert
        result.Should().BeTrue();
        item.Tags.Should().Contain("new-tag");
        item.Tags.Should().Contain("existing-tag");
    }

    [Fact]
    public async Task AddTagAsync_WithExistingTag_ReturnsTrue()
    {
        // Arrange
        ImageItem item = new()
        {
            Id = "1",
            Tags = new List<string> { "existing-tag" }
        };

        // Act
        bool result = await _service.AddTagAsync(item, "existing-tag");

        // Assert
        result.Should().BeTrue();
        item.Tags.Should().HaveCount(1);
    }

    [Fact]
    public async Task RemoveTagAsync_WithExistingTag_RemovesTag()
    {
        // Arrange
        ImageItem item = new()
        {
            Id = "1",
            Tags = new List<string> { "tag1", "tag2", "tag3" }
        };

        _mockHttpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = JsonContent.Create(new { })
            });

        // Act
        bool result = await _service.RemoveTagAsync(item, "tag2");

        // Assert
        result.Should().BeTrue();
        item.Tags.Should().NotContain("tag2");
        item.Tags.Should().Contain("tag1");
        item.Tags.Should().Contain("tag3");
    }

    [Fact]
    public async Task RemoveTagAsync_WithNonExistentTag_ReturnsTrue()
    {
        // Arrange
        ImageItem item = new()
        {
            Id = "1",
            Tags = new List<string> { "tag1" }
        };

        // Act
        bool result = await _service.RemoveTagAsync(item, "tag2");

        // Assert
        result.Should().BeTrue();
        item.Tags.Should().HaveCount(1);
    }

    [Fact]
    public async Task ToggleFavoriteAsync_TogglesFlag()
    {
        // Arrange
        ImageItem item = new()
        {
            Id = "1",
            IsFavorite = false
        };

        _mockHttpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = JsonContent.Create(new { })
            });

        // Act
        bool result = await _service.ToggleFavoriteAsync(item);

        // Assert
        result.Should().BeTrue();
        item.IsFavorite.Should().BeTrue();
    }

    [Fact]
    public async Task BulkUpdateAsync_SendsCorrectRequest()
    {
        // Arrange
        List<string> itemIds = new() { "1", "2", "3" };
        List<string> tagsToAdd = new() { "new-tag" };

        _mockHttpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Patch &&
                    req.RequestUri!.ToString().Contains("/bulk")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = JsonContent.Create(new { updatedCount = 3 })
            });

        // Act
        int result = await _service.BulkUpdateAsync(itemIds, tagsToAdd: tagsToAdd);

        // Assert
        result.Should().Be(3);
        foreach (string id in itemIds)
        {
            _service.DirtyItemIds.Should().NotContain(id);
        }
    }

    [Fact]
    public async Task BulkUpdateAsync_WithFailure_ReturnsZero()
    {
        // Arrange
        List<string> itemIds = new() { "1", "2" };

        _mockHttpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.BadRequest
            });

        // Act
        int result = await _service.BulkUpdateAsync(itemIds);

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public async Task UpdateItemAsync_UpdatesAllProvidedFields()
    {
        // Arrange
        ImageItem item = new()
        {
            Id = "1",
            Title = "Old Title",
            Description = "Old Description",
            Tags = new List<string> { "old-tag" },
            IsFavorite = false
        };

        _mockHttpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = JsonContent.Create(new { })
            });

        // Act
        await _service.UpdateItemAsync(
            item,
            title: "New Title",
            description: "New Description",
            tags: new List<string> { "new-tag" },
            isFavorite: true);

        // Assert
        item.Title.Should().Be("New Title");
        item.Description.Should().Be("New Description");
        item.Tags.Should().Contain("new-tag");
        item.IsFavorite.Should().BeTrue();
    }
}
