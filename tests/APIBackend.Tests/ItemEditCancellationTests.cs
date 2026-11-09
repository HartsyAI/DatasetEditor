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
// The Core repository interface is aliased for the same reason as in the other endpoint tests: the
// API layer has its own IDatasetItemRepository in the namespace the endpoints use.
using IDatasetRepository = DatasetStudio.Core.Abstractions.Repositories.IDatasetRepository;

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
            DatasetDto dataset = new() { Id = datasetId, Name = "edit" };
            DatasetItemDto item = new() { Id = itemId, DatasetId = datasetId, Title = "before" };
            using var cts = new CancellationTokenSource();
            CancellationToken token = cts.Token;

            var itemRepository = new Mock<IDatasetItemRepository>();
            itemRepository.Setup(r => r.GetItemAsync(itemId, token)).ReturnsAsync(item);

            var datasetRepository = new Mock<IDatasetRepository>();
            datasetRepository.Setup(r => r.GetByIdAsync(datasetId, token)).ReturnsAsync(dataset);

            var permissionService = new Mock<IDatasetPermissionService>();
            permissionService.Setup(p => p.CanReadAsync(dataset, It.IsAny<ClaimsPrincipal?>(), token)).ReturnsAsync(true);
            permissionService.Setup(p => p.CanWriteAsync(dataset, It.IsAny<ClaimsPrincipal?>(), token)).ReturnsAsync(true);

            await DatasetStudio.APIBackend.Endpoints.ItemEditEndpoints.UpdateItem(
                itemId,
                new UpdateItemRequest { Title = "after" },
                itemRepository.Object,
                datasetRepository.Object,
                permissionService.Object,
                new DefaultHttpContext(),
                NullLogger<Program>.Instance,
                token);

            itemRepository.Verify(r => r.GetItemAsync(itemId, token), Times.Once);
            datasetRepository.Verify(r => r.GetByIdAsync(datasetId, token), Times.Once);
            permissionService.Verify(p => p.CanReadAsync(dataset, It.IsAny<ClaimsPrincipal?>(), token), Times.Once);
            permissionService.Verify(p => p.CanWriteAsync(dataset, It.IsAny<ClaimsPrincipal?>(), token), Times.Once);
            itemRepository.Verify(r => r.UpdateItemAsync(It.IsAny<DatasetItemDto>(), token), Times.Once);
        }

        [Fact]
        public async Task BulkUpdateItems_PassesRequestCancellationTokenToEveryCall()
        {
            Guid datasetId = Guid.NewGuid();
            Guid itemId = Guid.NewGuid();
            DatasetDto dataset = new() { Id = datasetId, Name = "bulk" };
            DatasetItemDto item = new() { Id = itemId, DatasetId = datasetId, Title = "bulk" };
            using var cts = new CancellationTokenSource();
            CancellationToken token = cts.Token;

            var itemRepository = new Mock<IDatasetItemRepository>();
            itemRepository.Setup(r => r.GetItemAsync(itemId, token)).ReturnsAsync(item);

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
                datasetRepository.Object,
                permissionService.Object,
                new DefaultHttpContext(),
                NullLogger<Program>.Instance,
                token);

            itemRepository.Verify(r => r.GetItemAsync(itemId, token), Times.Once);
            datasetRepository.Verify(r => r.GetByIdAsync(datasetId, token), Times.Once);
            permissionService.Verify(p => p.CanWriteAsync(dataset, It.IsAny<ClaimsPrincipal?>(), token), Times.Once);
            itemRepository.Verify(r => r.UpdateItemsAsync(It.IsAny<IEnumerable<DatasetItemDto>>(), token), Times.Once);
        }
    }
}
