using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DatasetStudio.ClientApp.Features.Auth.Pages;
using DatasetStudio.ClientApp.Features.Auth.Services;
using DatasetStudio.ClientApp.Features.Datasets.Services;
using DatasetStudio.ClientApp.Services.ApiClients;
using DatasetStudio.ClientApp.Services.StateManagement;
using DatasetStudio.Core.DomainModels.Datasets;
using DatasetStudio.DTO.Datasets;
using Microsoft.AspNetCore.Components.Authorization;
using Xunit;

namespace DatasetStudio.Tests.ClientApp
{
    /// <summary>
    /// Regression tests for the client-correctness audit items (U-2, U-9, A-8). Each one fails on
    /// the code it replaced: A-8 and U-2 do not compile against the old signatures, and U-9 threw
    /// <see cref="HttpRequestException"/> out of the auth state provider.
    /// </summary>
    public sealed class ClientCorrectnessTests
    {
        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

            public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            {
                _respond = respond;
            }

            public HttpRequestMessage? LastRequest { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastRequest = request;
                return Task.FromResult(_respond(request));
            }
        }

        private static HttpClient BuildClient(StubHandler handler) =>
            new(handler) { BaseAddress = new Uri("http://localhost:5000/") };

        [Theory]
        [InlineData("/datasets/create")]
        [InlineData("/dataset-viewer?id=abc")]
        [InlineData("/datasets")]
        public void ResolveReturnUrl_AcceptsSameSitePaths(string returnUrl)
        {
            Assert.Equal(returnUrl, Login.ResolveReturnUrl(returnUrl));
        }

        [Theory]
        [InlineData("https://evil.example/phish")]
        [InlineData("//evil.example/phish")]
        [InlineData("/\\evil.example/phish")]
        [InlineData("javascript:alert(1)")]
        [InlineData("datasets/create")]
        [InlineData("")]
        [InlineData(null)]
        public void ResolveReturnUrl_RejectsOffSiteAndMalformedTargets(string? returnUrl)
        {
            // A-8: an absolute or protocol-relative returnUrl must never reach NavigateTo.
            Assert.Equal("/datasets", Login.ResolveReturnUrl(returnUrl));
        }

        [Fact]
        public async Task AuthStateProvider_WhenApiUnreachable_ReturnsAnonymousInsteadOfThrowing()
        {
            // U-9: an unreachable API must not escape as an exception, which would be fatal at startup.
            StubHandler handler = new(_ => throw new HttpRequestException("API is down"));
            CookieAuthenticationStateProvider provider = new(new AuthApiClient(BuildClient(handler)));

            AuthenticationState state = await provider.GetAuthenticationStateAsync();

            Assert.False(state.User.Identity?.IsAuthenticated ?? false);
        }

        [Fact]
        public async Task AuthStateProvider_WhenApiTimesOut_ReturnsAnonymous()
        {
            StubHandler handler = new(_ => throw new TaskCanceledException("timed out"));
            CookieAuthenticationStateProvider provider = new(new AuthApiClient(BuildClient(handler)));

            AuthenticationState state = await provider.GetAuthenticationStateAsync();

            Assert.False(state.User.Identity?.IsAuthenticated ?? false);
        }

        [Fact]
        public async Task ItemEditService_UpdateItem_PatchesThroughTypedClient()
        {
            // U-2: the edit must go through DatasetApiClient (cookie handler, API base address).
            // The old raw HttpClient targeted the client origin, so the PATCH never reached the API.
            Guid itemId = Guid.NewGuid();
            StubHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK));
            DatasetApiClient apiClient = new(BuildClient(handler));
            ItemEditService service = new(apiClient, new DatasetState());
            DatasetItemDto item = new() { Id = itemId, Title = "before" };

            bool saved = await service.UpdateItemAsync(item, title: "after");

            Assert.True(saved);
            Assert.NotNull(handler.LastRequest);
            Assert.Equal(HttpMethod.Patch, handler.LastRequest!.Method);
            Assert.Equal("/api/items/" + itemId, handler.LastRequest.RequestUri!.AbsolutePath);
            Assert.Equal("http://localhost:5000", handler.LastRequest.RequestUri.GetLeftPart(UriPartial.Authority));
        }

        [Fact]
        public async Task ItemEditService_UpdateItem_ReturnsFalseWhenApiRejects()
        {
            StubHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
            ItemEditService service = new(new DatasetApiClient(BuildClient(handler)), new DatasetState());

            bool saved = await service.UpdateItemAsync(new DatasetItemDto { Id = Guid.NewGuid() }, title: "x");

            Assert.False(saved);
        }

        [Fact]
        public void DatasetState_UpdateItem_KeepsEditedItemSelected()
        {
            // Codex P2 on PR #6: replacing the item left the old DTO in SelectedItems, so the edited
            // item read as unselected and a second toggle added a duplicate. It fails on that code.
            Guid itemId = Guid.NewGuid();
            DatasetItemDto original = new() { Id = itemId, Title = "before" };
            DatasetState state = new();
            state.LoadDataset(new Dataset { Id = "ds", Name = "test" }, new List<DatasetItemDto> { original });
            state.ToggleSelection(original);

            DatasetItemDto edited = new() { Id = itemId, Title = "after" };
            state.UpdateItem(edited);

            Assert.True(state.IsSelected(edited));
            Assert.Equal(1, state.SelectedCount);

            state.ToggleSelection(edited);

            Assert.Equal(0, state.SelectedCount);
        }
    }
}
