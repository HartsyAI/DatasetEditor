using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using DatasetStudio.APIBackend.Endpoints;
using DatasetStudio.APIBackend.Services.Auth;
using DatasetStudio.APIBackend.Services.DatasetManagement;
using DatasetStudio.DTO.Datasets;
using DatasetStudio.DTO.Items;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using DatasetStudio.APIBackend.DataAccess.PostgreSQL.Repositories;

namespace DatasetStudio.Tests.APIBackend
{
    /// <summary>
    /// Audit E-2: the item edit handlers take the request's CancellationToken and pass it to every
    /// repository and permission call, so a client that disconnects stops the work instead of
    /// letting the handler run to completion against a database it no longer needs.
    /// </summary>
    public sealed class ItemEditCancellationTests
    {
        [Fact]
        public async Task UpdateItem_PassesRequestCancellationTokenToEveryCall()
        {
            Guid datasetId = Guid.NewGuid();
            Guid itemId = Guid.NewGuid();
            DatasetRecord dataset = new() { Id = datasetId, Name = "edit" };
            DatasetItemDto item = new() { Id = itemId, DatasetId = datasetId, Title = "before" };
            using var cts = new CancellationTokenSource();
            CancellationToken token = cts.Token;

            var itemRepository = new Mock<IDatasetItemRepository>();
            itemRepository.Setup(r => r.GetItemAsync(itemId, token)).ReturnsAsync(item);
            itemRepository
                .Setup(r => r.EditItemAsync(datasetId, itemId, It.IsAny<Func<DatasetItemDto, bool>?>(), It.IsAny<Func<DatasetItemDto, DatasetItemDto>>(), token))
                .ReturnsAsync(new ItemEditResult(ItemEditStatus.Updated, item with { Title = "after" }));

            var datasetRepository = new Mock<IDatasetRepository>();
            datasetRepository.Setup(r => r.GetByIdAsync(datasetId, token)).ReturnsAsync(dataset);

            var permissionService = new Mock<IDatasetPermissionService>();
            permissionService.Setup(p => p.CanReadAsync(dataset, It.IsAny<ClaimsPrincipal?>(), token)).ReturnsAsync(true);
            permissionService.Setup(p => p.CanWriteAsync(dataset, It.IsAny<ClaimsPrincipal?>(), token)).ReturnsAsync(true);

            await DatasetStudio.APIBackend.Endpoints.ItemEditEndpoints.UpdateItem(
                itemId,
                new UpdateItemRequest { Title = "after" },
                itemRepository.Object,
                new Mock<DatasetStudio.APIBackend.DataAccess.PostgreSQL.Repositories.IItemOverlayRepository>(MockBehavior.Strict).Object,
                datasetRepository.Object,
                permissionService.Object,
                new DefaultHttpContext(),
                NullLogger<Program>.Instance,
                token);

            itemRepository.Verify(r => r.GetItemAsync(itemId, token), Times.Once);
            datasetRepository.Verify(r => r.GetByIdAsync(datasetId, token), Times.Once);
            permissionService.Verify(p => p.CanReadAsync(dataset, It.IsAny<ClaimsPrincipal?>(), token), Times.Once);
            permissionService.Verify(p => p.CanWriteAsync(dataset, It.IsAny<ClaimsPrincipal?>(), token), Times.Once);
            itemRepository.Verify(r => r.EditItemAsync(datasetId, itemId, It.IsAny<Func<DatasetItemDto, bool>?>(), It.IsAny<Func<DatasetItemDto, DatasetItemDto>>(), token), Times.Once);
        }

        [Fact]
        public async Task BulkUpdateItems_PassesRequestCancellationTokenToEveryCall()
        {
            Guid datasetId = Guid.NewGuid();
            Guid itemId = Guid.NewGuid();
            DatasetRecord dataset = new() { Id = datasetId, Name = "bulk" };
            DatasetItemDto item = new() { Id = itemId, DatasetId = datasetId, Title = "bulk" };
            using var cts = new CancellationTokenSource();
            CancellationToken token = cts.Token;

            var itemRepository = new Mock<IDatasetItemRepository>();
            itemRepository.Setup(r => r.GetItemAsync(itemId, token)).ReturnsAsync(item);
            itemRepository
                .Setup(r => r.EditItemsAsync(datasetId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<Func<DatasetItemDto, DatasetItemDto>>(), token))
                .ReturnsAsync(new List<DatasetItemDto> { item with { IsFavorite = true } });

            var datasetRepository = new Mock<IDatasetRepository>();
            datasetRepository.Setup(r => r.GetByIdAsync(datasetId, token)).ReturnsAsync(dataset);

            var permissionService = new Mock<IDatasetPermissionService>();
            permissionService.Setup(p => p.CanWriteAsync(dataset, It.IsAny<ClaimsPrincipal?>(), token)).ReturnsAsync(true);

            BulkUpdateItemsRequest request = new()
            {
                ItemIds = new List<Guid> { itemId },
                SetFavorite = true,
            };

            await DatasetStudio.APIBackend.Endpoints.ItemEditEndpoints.BulkUpdateItems(
                request,
                itemRepository.Object,
                new Mock<DatasetStudio.APIBackend.DataAccess.PostgreSQL.Repositories.IItemOverlayRepository>(MockBehavior.Strict).Object,
                datasetRepository.Object,
                permissionService.Object,
                new DefaultHttpContext(),
                NullLogger<Program>.Instance,
                token);

            itemRepository.Verify(r => r.GetItemAsync(itemId, token), Times.Once);
            datasetRepository.Verify(r => r.GetByIdAsync(datasetId, token), Times.Once);
            permissionService.Verify(p => p.CanWriteAsync(dataset, It.IsAny<ClaimsPrincipal?>(), token), Times.Once);
            itemRepository.Verify(r => r.EditItemsAsync(datasetId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<Func<DatasetItemDto, DatasetItemDto>>(), token), Times.Once);
        }
    }
}
