using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using DatasetStudio.APIBackend.DataAccess.PostgreSQL;
using DatasetStudio.DTO.Auth;
using DatasetStudio.DTO.Common;
using DatasetStudio.DTO.Datasets;
using DatasetStudio.Tests.APIBackend.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace DatasetStudio.Tests.APIBackend
{
    /// <summary>
    /// Sharing a dataset (audit D-2): the grant table existed and the permission checks read it,
    /// but nothing could create a grant, so a private dataset could never be shared. Also covers
    /// dropping the unused dataset_items and captions tables (D-7).
    /// </summary>
    [Collection(PostgresCollection.Name)]
    public sealed class DatasetSharingTests : IAsyncLifetime
    {
        private readonly PostgresContainerFixture _postgres;
        private DatasetStudioApiFactory _factory = default!;

        public DatasetSharingTests(PostgresContainerFixture postgres)
        {
            _postgres = postgres;
        }

        public async Task InitializeAsync()
        {
            _factory = new DatasetStudioApiFactory(_postgres.ConnectionString);
            await _factory.InitializeDatabaseAsync();
        }

        public async Task DisposeAsync()
        {
            await _factory.DisposeAsync();
            await _factory.DropDatabaseAsync();
        }

        private async Task<(HttpClient Client, AuthUserDto User)> SignUpAsync(string username)
        {
            HttpClient client = _factory.CreateClient();
            HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/register",
                new RegisterRequest(username, $"{username}@example.com", "password123"));
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            return (client, (await response.Content.ReadFromJsonAsync<AuthUserDto>())!);
        }

        private static async Task<DatasetDetailDto> CreatePrivateDatasetAsync(HttpClient owner, string name)
        {
            HttpResponseMessage response = await owner.PostAsJsonAsync("/api/datasets", new CreateDatasetRequest(name, null));
            response.StatusCode.Should().Be(HttpStatusCode.Created);
            return (await response.Content.ReadFromJsonAsync<DatasetDetailDto>())!;
        }

        private static Task<HttpResponseMessage> ShareAsync(HttpClient client, Guid datasetId, ShareDatasetRequest request) =>
            client.PutAsJsonAsync($"/api/datasets/{datasetId}/shares", request);

        private static async Task<string?> ErrorOfAsync(HttpResponseMessage response)
        {
            Dictionary<string, string>? body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
            return body?.GetValueOrDefault("error");
        }

        private static Task<HttpResponseMessage> UploadCsvAsync(HttpClient client, Guid datasetId)
        {
            MultipartFormDataContent form = new();
            ByteArrayContent file = new(Encoding.UTF8.GetBytes("image_url,title\nhttps://example.com/a.jpg,a\n"));
            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");
            form.Add(file, "file", "items.csv");
            return client.PostAsync($"/api/datasets/{datasetId}/upload", form);
        }

        private static async Task<List<Guid>> LibraryIdsAsync(HttpClient client)
        {
            using System.Text.Json.JsonDocument page = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync("/api/datasets?pageSize=100"));
            return page.RootElement.GetProperty("datasets").EnumerateArray().Select(d => d.GetProperty("id").GetGuid()).ToList();
        }

        [Fact]
        public async Task Owner_SharesPrivateDatasetForReading_GranteeCanOpenAndListButNotWrite()
        {
            (HttpClient owner, _) = await SignUpAsync("sh-owner1");
            (HttpClient reader, AuthUserDto readerUser) = await SignUpAsync("sh-reader1");
            DatasetDetailDto dataset = await CreatePrivateDatasetAsync(owner, "private-shared");

            (await reader.GetAsync($"/api/datasets/{dataset.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await LibraryIdsAsync(reader)).Should().NotContain(dataset.Id);

            HttpResponseMessage shared = await ShareAsync(owner, dataset.Id, new ShareDatasetRequest("sh-reader1"));
            shared.StatusCode.Should().Be(HttpStatusCode.OK);
            DatasetShareDto share = (await shared.Content.ReadFromJsonAsync<DatasetShareDto>())!;
            share.UserId.Should().Be(readerUser.Id);
            share.AccessLevel.Should().Be(DatasetAccessLevel.Read);
            share.GrantedByUsername.Should().Be("sh-owner1");

            DatasetDetailDto? seen = await reader.GetFromJsonAsync<DatasetDetailDto>($"/api/datasets/{dataset.Id}");
            seen!.CanShare.Should().BeFalse();
            (await LibraryIdsAsync(reader)).Should().Contain(dataset.Id);
            (await UploadCsvAsync(reader, dataset.Id)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await reader.GetAsync($"/api/datasets/{dataset.Id}/shares")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

            DatasetDetailDto? ownerView = await owner.GetFromJsonAsync<DatasetDetailDto>($"/api/datasets/{dataset.Id}");
            ownerView!.CanShare.Should().BeTrue();
        }

        [Fact]
        public async Task WriteShare_AllowsUpload_AndUpdatingTheShareChangesItInPlace()
        {
            (HttpClient owner, _) = await SignUpAsync("sh-owner2");
            (HttpClient writer, AuthUserDto writerUser) = await SignUpAsync("sh-writer2");
            DatasetDetailDto dataset = await CreatePrivateDatasetAsync(owner, "write-shared");

            (await ShareAsync(owner, dataset.Id, new ShareDatasetRequest("sh-writer2"))).StatusCode.Should().Be(HttpStatusCode.OK);
            (await UploadCsvAsync(writer, dataset.Id)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

            (await ShareAsync(owner, dataset.Id, new ShareDatasetRequest("sh-writer2@example.com", DatasetAccessLevel.Write))).StatusCode.Should().Be(HttpStatusCode.OK);
            (await UploadCsvAsync(writer, dataset.Id)).StatusCode.Should().Be(HttpStatusCode.Accepted);

            List<DatasetShareDto>? shares = await owner.GetFromJsonAsync<List<DatasetShareDto>>($"/api/datasets/{dataset.Id}/shares");
            shares.Should().ContainSingle().Which.Should().Match<DatasetShareDto>(s => s.UserId == writerUser.Id && s.AccessLevel == DatasetAccessLevel.Write);
        }

        [Fact]
        public async Task Revoke_EndsAccess_AndExpiredSharesDontCount()
        {
            (HttpClient owner, _) = await SignUpAsync("sh-owner3");
            (HttpClient guest, AuthUserDto guestUser) = await SignUpAsync("sh-guest3");
            DatasetDetailDto dataset = await CreatePrivateDatasetAsync(owner, "revoked");

            await ShareAsync(owner, dataset.Id, new ShareDatasetRequest("sh-guest3"));
            (await guest.GetAsync($"/api/datasets/{dataset.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);

            (await owner.DeleteAsync($"/api/datasets/{dataset.Id}/shares/{guestUser.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await guest.GetAsync($"/api/datasets/{dataset.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await owner.DeleteAsync($"/api/datasets/{dataset.Id}/shares/{guestUser.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

            // A share that has run out neither opens nor lists the dataset.
            await ShareAsync(owner, dataset.Id, new ShareDatasetRequest("sh-guest3", ExpiresAt: DateTime.UtcNow.AddMinutes(5)));
            using (IServiceScope scope = _factory.Services.CreateScope())
            {
                DatasetStudioDbContext db = scope.ServiceProvider.GetRequiredService<DatasetStudioDbContext>();
                await db.Permissions.Where(p => p.DatasetId == dataset.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
            }
            (await guest.GetAsync($"/api/datasets/{dataset.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await LibraryIdsAsync(guest)).Should().NotContain(dataset.Id);
        }

        [Fact]
        public async Task Grantee_CanLeave_ButCantRevokeOthers()
        {
            (HttpClient owner, _) = await SignUpAsync("sh-owner4");
            (HttpClient a, AuthUserDto aUser) = await SignUpAsync("sh-a4");
            (_, AuthUserDto bUser) = await SignUpAsync("sh-b4");
            DatasetDetailDto dataset = await CreatePrivateDatasetAsync(owner, "leave");
            await ShareAsync(owner, dataset.Id, new ShareDatasetRequest("sh-a4"));
            await ShareAsync(owner, dataset.Id, new ShareDatasetRequest("sh-b4"));

            (await a.DeleteAsync($"/api/datasets/{dataset.Id}/shares/{bUser.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await a.DeleteAsync($"/api/datasets/{dataset.Id}/shares/{aUser.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await a.GetAsync($"/api/datasets/{dataset.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task ResharingGrantee_CantGrantMoreThanTheyHold_OrChangeOthersShares()
        {
            (HttpClient owner, _) = await SignUpAsync("sh-owner5");
            (HttpClient sharer, _) = await SignUpAsync("sh-sharer5");
            (HttpClient third, AuthUserDto thirdUser) = await SignUpAsync("sh-third5");
            await SignUpAsync("sh-fourth5");
            DatasetDetailDto dataset = await CreatePrivateDatasetAsync(owner, "reshare");
            await ShareAsync(owner, dataset.Id, new ShareDatasetRequest("sh-sharer5", DatasetAccessLevel.Read, CanShare: true));
            await ShareAsync(owner, dataset.Id, new ShareDatasetRequest("sh-fourth5"));

            HttpResponseMessage tooMuch = await ShareAsync(sharer, dataset.Id, new ShareDatasetRequest("sh-third5", DatasetAccessLevel.Write));
            tooMuch.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await ErrorOfAsync(tooMuch)).Should().Contain("read-only");

            HttpResponseMessage delete = await ShareAsync(sharer, dataset.Id, new ShareDatasetRequest("sh-third5", CanDelete: true));
            delete.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            (await ShareAsync(sharer, dataset.Id, new ShareDatasetRequest("sh-third5"))).StatusCode.Should().Be(HttpStatusCode.OK);
            (await third.GetAsync($"/api/datasets/{dataset.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);

            // The owner granted sh-fourth5's share, so the resharer can neither change nor end it,
            // but can end the one they granted.
            (await ShareAsync(sharer, dataset.Id, new ShareDatasetRequest("sh-fourth5", CanShare: true))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            List<DatasetShareDto>? shares = await sharer.GetFromJsonAsync<List<DatasetShareDto>>($"/api/datasets/{dataset.Id}/shares");
            Guid fourthId = shares!.Single(s => s.Username == "sh-fourth5").UserId;
            (await sharer.DeleteAsync($"/api/datasets/{dataset.Id}/shares/{fourthId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await sharer.DeleteAsync($"/api/datasets/{dataset.Id}/shares/{thirdUser.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        /// <summary>
        /// PR 5.2 review: a user sharing through an expiring share could give a share with no end
        /// (or a later one), keeping access through another account after their own ran out.
        /// </summary>
        [Fact]
        public async Task ResharingThroughAnExpiringShare_CantOutlastIt()
        {
            (HttpClient owner, _) = await SignUpAsync("sh-owner8");
            (HttpClient sharer, _) = await SignUpAsync("sh-sharer8");
            await SignUpAsync("sh-third8");
            DatasetDetailDto dataset = await CreatePrivateDatasetAsync(owner, "expiring-reshare");
            DateTime ownEnd = DateTime.UtcNow.AddDays(1);
            await ShareAsync(owner, dataset.Id, new ShareDatasetRequest("sh-sharer8", CanShare: true, ExpiresAt: ownEnd));

            HttpResponseMessage forever = await ShareAsync(sharer, dataset.Id, new ShareDatasetRequest("sh-third8"));
            forever.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await ErrorOfAsync(forever)).Should().Contain("Your own access ends on");

            (await ShareAsync(sharer, dataset.Id, new ShareDatasetRequest("sh-third8", ExpiresAt: ownEnd.AddDays(1)))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await ShareAsync(sharer, dataset.Id, new ShareDatasetRequest("sh-third8", ExpiresAt: ownEnd.AddHours(-6)))).StatusCode.Should().Be(HttpStatusCode.OK);

            // The owner isn't bound by anyone's end date.
            (await ShareAsync(owner, dataset.Id, new ShareDatasetRequest("sh-third8"))).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        /// <summary>
        /// PR 5.2 review: the "change only shares you granted" rule was checked before the write, so
        /// a delegated sharer racing the owner could overwrite the owner's new share and take it over.
        /// The rule is now part of the write (and of the delete); this calls the repository directly,
        /// as the endpoint does once its earlier checks have passed.
        /// </summary>
        [Fact]
        public async Task DelegatedUpsertAndDelete_LeaveAShareSomeoneElseGranted()
        {
            (HttpClient owner, AuthUserDto ownerUser) = await SignUpAsync("sh-owner9");
            (_, AuthUserDto sharerUser) = await SignUpAsync("sh-sharer9");
            (_, AuthUserDto targetUser) = await SignUpAsync("sh-target9");
            DatasetDetailDto dataset = await CreatePrivateDatasetAsync(owner, "atomic");
            await ShareAsync(owner, dataset.Id, new ShareDatasetRequest("sh-target9", DatasetAccessLevel.Write));

            using IServiceScope scope = _factory.Services.CreateScope();
            Core.Abstractions.Repositories.IPermissionRepository grants =
                scope.ServiceProvider.GetRequiredService<Core.Abstractions.Repositories.IPermissionRepository>();

            bool replaced = await grants.UpsertAsync(new PermissionDto
            {
                DatasetId = dataset.Id,
                UserId = targetUser.Id,
                AccessLevel = "Read",
                GrantedByUserId = sharerUser.Id
            }, onlyReplacingGrantsBy: sharerUser.Id);
            replaced.Should().BeFalse();

            bool deleted = await grants.DeleteAsync(dataset.Id, targetUser.Id, onlyIfGrantedBy: sharerUser.Id);
            deleted.Should().BeFalse();

            PermissionDto? kept = await grants.GetAsync(dataset.Id, targetUser.Id);
            kept!.GrantedByUserId.Should().Be(ownerUser.Id);
            kept.AccessLevel.Should().Be("Write");

            // Without the restriction (the owner's own call) the same upsert replaces it.
            (await grants.UpsertAsync(kept with { AccessLevel = "Read" }, onlyReplacingGrantsBy: null)).Should().BeTrue();
            (await grants.GetAsync(dataset.Id, targetUser.Id))!.AccessLevel.Should().Be("Read");
        }

        /// <summary>
        /// PR 5.2 review: a username containing '@' (allowed before registration validation) was
        /// looked up only as an email, so sharing with it, or editing its share from the dialog
        /// (which fills in the username), always said no such account.
        /// </summary>
        [Fact]
        public async Task Share_WithAUsernameContainingAt_FindsTheAccount()
        {
            (HttpClient owner, _) = await SignUpAsync("sh-owner10");
            DatasetDetailDto dataset = await CreatePrivateDatasetAsync(owner, "at-username");
            using (IServiceScope scope = _factory.Services.CreateScope())
            {
                Core.Abstractions.Repositories.IUserRepository users =
                    scope.ServiceProvider.GetRequiredService<Core.Abstractions.Repositories.IUserRepository>();
                await users.CreateAsync(new UserDto { Id = Guid.NewGuid(), Username = "old@name", Email = "different@example.com", PasswordHash = "x" });
            }

            HttpResponseMessage shared = await ShareAsync(owner, dataset.Id, new ShareDatasetRequest("old@name"));

            shared.StatusCode.Should().Be(HttpStatusCode.OK);
            (await shared.Content.ReadFromJsonAsync<DatasetShareDto>())!.Username.Should().Be("old@name");
        }

        [Fact]
        public async Task Share_RejectsUnknownAccountsOwnerSelfAndPastDates()
        {
            (HttpClient owner, _) = await SignUpAsync("sh-owner6");
            await SignUpAsync("sh-other6");
            DatasetDetailDto dataset = await CreatePrivateDatasetAsync(owner, "bad-requests");

            HttpResponseMessage unknown = await ShareAsync(owner, dataset.Id, new ShareDatasetRequest("nobody-at-all"));
            unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await ErrorOfAsync(unknown)).Should().Contain("No active account");

            (await ShareAsync(owner, dataset.Id, new ShareDatasetRequest("sh-owner6"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await ShareAsync(owner, dataset.Id, new ShareDatasetRequest(" "))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await ShareAsync(owner, dataset.Id, new ShareDatasetRequest("sh-other6", ExpiresAt: DateTime.UtcNow.AddDays(-1)))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await ShareAsync(owner, dataset.Id, new ShareDatasetRequest("sh-other6", (DatasetAccessLevel)7))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task Strangers_GetNotFound_AndAnonymousGets401()
        {
            (HttpClient owner, AuthUserDto ownerUser) = await SignUpAsync("sh-owner7");
            (HttpClient stranger, _) = await SignUpAsync("sh-stranger7");
            DatasetDetailDto dataset = await CreatePrivateDatasetAsync(owner, "hidden");

            (await stranger.GetAsync($"/api/datasets/{dataset.Id}/shares")).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await ShareAsync(stranger, dataset.Id, new ShareDatasetRequest("sh-stranger7"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await stranger.DeleteAsync($"/api/datasets/{dataset.Id}/shares/{ownerUser.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

            using HttpClient anonymous = _factory.CreateClient();
            (await anonymous.GetAsync($"/api/datasets/{dataset.Id}/shares")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task UnusedItemAndCaptionTables_AreGone()
        {
            using IServiceScope scope = _factory.Services.CreateScope();
            DatasetStudioDbContext db = scope.ServiceProvider.GetRequiredService<DatasetStudioDbContext>();
            await db.Database.OpenConnectionAsync();
            await using NpgsqlCommand command = new(
                "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name IN ('dataset_items', 'captions')",
                (NpgsqlConnection)db.Database.GetDbConnection());
            long count = (long)(await command.ExecuteScalarAsync())!;
            count.Should().Be(0);
        }
    }
}
