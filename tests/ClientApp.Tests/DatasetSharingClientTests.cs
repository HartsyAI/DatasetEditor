using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DatasetStudio.ClientApp.Services.ApiClients;
using DatasetStudio.DTO.Datasets;
using Xunit;

namespace DatasetStudio.Tests.ClientApp;

/// <summary>
/// PR 5.2 (audit D-2): the share dialog shows the server's reason when a share is refused, and
/// the calls go to the share routes through the cookie-carrying API client.
/// </summary>
public sealed class DatasetSharingClientTests
{
    private static readonly Guid DatasetId = Guid.NewGuid();

    [Fact]
    public async Task Share_Refused_ReturnsTheServersReason()
    {
        FakeApi api = new(HttpStatusCode.Forbidden, "{\"error\":\"Your own access is read-only, so you can share this dataset only for reading.\"}");
        DatasetApiClient client = Create(api);

        (DatasetShareDto? share, string? error) = await client.ShareDatasetAsync(DatasetId, new ShareDatasetRequest("bob", DatasetAccessLevel.Write));

        Assert.Null(share);
        Assert.Equal("Your own access is read-only, so you can share this dataset only for reading.", error);
        Assert.Equal("PUT", api.Method);
        Assert.Equal($"/api/datasets/{DatasetId}/shares", api.Path);
        Assert.Contains("\"accessLevel\":1", api.Body);
    }

    [Fact]
    public async Task Share_RefusedWithoutABody_StillSaysSomething()
    {
        DatasetApiClient client = Create(new FakeApi(HttpStatusCode.Forbidden, string.Empty));

        (DatasetShareDto? share, string? error) = await client.ShareDatasetAsync(DatasetId, new ShareDatasetRequest("bob"));

        Assert.Null(share);
        Assert.Equal("Sharing failed (HTTP 403).", error);
    }

    [Fact]
    public async Task Share_Accepted_ReturnsTheShare()
    {
        Guid userId = Guid.NewGuid();
        DatasetApiClient client = Create(new FakeApi(HttpStatusCode.OK, $"{{\"userId\":\"{userId}\",\"username\":\"bob\",\"accessLevel\":1,\"canDelete\":false,\"canShare\":true}}"));

        (DatasetShareDto? share, string? error) = await client.ShareDatasetAsync(DatasetId, new ShareDatasetRequest("bob", DatasetAccessLevel.Write, CanShare: true));

        Assert.Null(error);
        Assert.Equal(userId, share!.UserId);
        Assert.Equal(DatasetAccessLevel.Write, share.AccessLevel);
        Assert.True(share.CanShare);
    }

    [Fact]
    public async Task Revoke_DeletesTheUsersShare()
    {
        Guid userId = Guid.NewGuid();
        FakeApi api = new(HttpStatusCode.NoContent, string.Empty);

        Assert.True(await Create(api).RevokeShareAsync(DatasetId, userId));
        Assert.Equal("DELETE", api.Method);
        Assert.Equal($"/api/datasets/{DatasetId}/shares/{userId}", api.Path);
    }

    private static DatasetApiClient Create(FakeApi api) =>
        new(new HttpClient(api) { BaseAddress = new Uri("http://localhost:5000/") });

    /// <summary>Answers every request with one status and body, and records the request.</summary>
    private sealed class FakeApi : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public FakeApi(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        public string? Method { get; private set; }
        public string? Path { get; private set; }
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method.Method;
            Path = request.RequestUri!.AbsolutePath;
            Body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            };
        }
    }
}
