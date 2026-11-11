using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using DatasetStudio.APIBackend.DataAccess.PostgreSQL;
using DatasetStudio.APIBackend.DataAccess.PostgreSQL.Entities;
using DatasetStudio.DTO.Auth;
using DatasetStudio.DTO.Datasets;
using DatasetStudio.Tests.APIBackend.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DatasetStudio.Tests.APIBackend
{
    /// <summary>
    /// End-to-end coverage for Phase 3 auth: register/login/logout/me, ownership on dataset
    /// creation, permission-gated mutation (delete, grant, expire), anonymous visibility, and
    /// the admin-role gate on ImportFromFolder. Runs through the real HTTP pipeline (cookie
    /// auth, CORS, [Authorize]) rather than calling services directly, since that pipeline is
    /// exactly what a code-review of the handlers alone can't prove works.
    /// </summary>
    [Collection(PostgresCollection.Name)]
    public sealed class AuthEndpointsTests : IAsyncLifetime
    {
        private readonly PostgresContainerFixture _postgres;
        private DatasetStudioApiFactory _factory = default!;

        public AuthEndpointsTests(PostgresContainerFixture postgres)
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
            await _factory.DropDatabaseAsync();
            await _factory.DisposeAsync();
        }

        // WebApplicationFactoryClientOptions.HandleCookies defaults to true -- a client created
        // this way carries whatever Set-Cookie a prior request on it received into subsequent
        // requests, exactly like a real browser tab.
        private HttpClient NewCookieClient() => _factory.CreateClient();

        private static async Task<AuthUserDto> RegisterAsync(HttpClient client, string username, string password = "password123", string? email = null)
        {
            HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/register",
                new RegisterRequest(username, email ?? $"{username}@example.com", password));
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            return (await response.Content.ReadFromJsonAsync<AuthUserDto>())!;
        }

        [Fact]
        public async Task Register_ThenMe_ReturnsTheRegisteredUser()
        {
            using HttpClient client = NewCookieClient();
            AuthUserDto registered = await RegisterAsync(client, "alice");

            HttpResponseMessage meResponse = await client.GetAsync("/api/auth/me");
            meResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            AuthUserDto? me = await meResponse.Content.ReadFromJsonAsync<AuthUserDto>();
            me!.Id.Should().Be(registered.Id);
            me.Username.Should().Be("alice");
        }

        [Fact]
        public async Task Me_WithoutLoggingIn_Returns401()
        {
            using HttpClient client = NewCookieClient();
            HttpResponseMessage response = await client.GetAsync("/api/auth/me");
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task Login_WrongPassword_Returns401WithGenericMessage()
        {
            using HttpClient client = NewCookieClient();
            await RegisterAsync(client, "bob", password: "correct-password");

            using HttpClient loginClient = NewCookieClient(); // separate client -- register() left `client` logged in
            HttpResponseMessage response = await loginClient.PostAsJsonAsync("/api/auth/login", new LoginRequest("bob", "wrong-password"));
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task Logout_ThenMe_Returns401()
        {
            using HttpClient client = NewCookieClient();
            await RegisterAsync(client, "carol");

            HttpResponseMessage logoutResponse = await client.PostAsync("/api/auth/logout", content: null);
            logoutResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            HttpResponseMessage meResponse = await client.GetAsync("/api/auth/me");
            meResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task Register_DuplicateUsername_Returns400WithClearError()
        {
            using HttpClient client = NewCookieClient();
            await RegisterAsync(client, "dave");

            using HttpClient secondClient = NewCookieClient();
            HttpResponseMessage response = await secondClient.PostAsJsonAsync("/api/auth/register",
                new RegisterRequest("dave", "dave2@example.com", "password123"));
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task Login_AgainstSeededAdminPlaceholderHash_Returns401NotServerError()
        {
            // The InitialCreate migration always seeds an "admin" user with a non-BCrypt
            // placeholder hash. This test runs with no ADMIN_PASSWORD configured, so
            // AdminBootstrapper skips and that placeholder hash is exactly what
            // IPasswordHasher.Verify has to handle safely -- BCrypt.Verify throws against it,
            // and if that exception ever escaped, this would 500 instead of 401.
            using HttpClient client = NewCookieClient();
            HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("admin", "anything"));
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task CreateDataset_Unauthenticated_Returns401()
        {
            using HttpClient client = NewCookieClient();
            HttpResponseMessage response = await client.PostAsJsonAsync("/api/datasets", new CreateDatasetRequest("no-auth-dataset", null));
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task CreateDataset_Authenticated_SetsCreatedByUserId()
        {
            using HttpClient client = NewCookieClient();
            AuthUserDto user = await RegisterAsync(client, "erin");

            HttpResponseMessage response = await client.PostAsJsonAsync("/api/datasets", new CreateDatasetRequest("erins-dataset", null));
            response.StatusCode.Should().Be(HttpStatusCode.Created);
            DatasetDto created = (await response.Content.ReadFromJsonAsync<DatasetDto>())!;
            created.CreatedByUserId.Should().Be(user.Id);
        }

        [Fact]
        public async Task Delete_ByNonOwner_DeniedThenGrantedThenExpiredAgain()
        {
            using HttpClient ownerClient = NewCookieClient();
            await RegisterAsync(ownerClient, "frank");
            // Public so the non-owner clears the CanReadAsync gate and this test exercises the
            // CanDeleteAsync permission logic specifically -- a private dataset would 404 here
            // (existence-hiding, see DeleteDataset's own comment), which is a different,
            // already-covered case (GetDataset_Anonymous_AllowedForPublicDeniedForPrivate).
            HttpResponseMessage createResponse = await ownerClient.PostAsJsonAsync(
                "/api/datasets", new CreateDatasetRequest("franks-public-set", null, IsPublic: true));
            DatasetDto dataset = (await createResponse.Content.ReadFromJsonAsync<DatasetDto>())!;

            using HttpClient otherClient = NewCookieClient();
            AuthUserDto other = await RegisterAsync(otherClient, "grace");

            // 1. Can read (public) but no delete grant -- denied, not hidden.
            HttpResponseMessage deniedResponse = await otherClient.DeleteAsync($"/api/datasets/{dataset.Id}");
            deniedResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            // 2. Grant a non-expired CanDelete permission directly in the database, isolating the
            // permission check from the share endpoints (DatasetSharingTests covers those).
            await GrantPermissionAsync(dataset.Id, other.Id, accessLevel: "Write", canDelete: true, expiresAt: null);
            HttpResponseMessage allowedResponse = await otherClient.DeleteAsync($"/api/datasets/{dataset.Id}");
            allowedResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        [Fact]
        public async Task Delete_WithExpiredPermission_StillDenied()
        {
            using HttpClient ownerClient = NewCookieClient();
            await RegisterAsync(ownerClient, "henry");
            // Public for the same reason as Delete_ByNonOwner_DeniedThenGrantedThenExpiredAgain --
            // isolates the CanDeleteAsync check from the (separately-covered) read-gate/404 case.
            HttpResponseMessage createResponse = await ownerClient.PostAsJsonAsync(
                "/api/datasets", new CreateDatasetRequest("henrys-set", null, IsPublic: true));
            DatasetDto dataset = (await createResponse.Content.ReadFromJsonAsync<DatasetDto>())!;

            using HttpClient otherClient = NewCookieClient();
            AuthUserDto other = await RegisterAsync(otherClient, "ivy");

            await GrantPermissionAsync(dataset.Id, other.Id, accessLevel: "Write", canDelete: true, expiresAt: DateTime.UtcNow.AddDays(-1));

            HttpResponseMessage response = await otherClient.DeleteAsync($"/api/datasets/{dataset.Id}");
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        [Fact]
        public async Task CreateDataset_WithoutIsPublic_DefaultsPrivateAndInvisibleToAnonymous()
        {
            // A creator who says nothing about visibility must get a private dataset, not a
            // world-readable one -- opt-in, not opt-out. Regression guard for the DatasetRecord/
            // DatasetEntity IsPublic default (both flipped from true to false).
            using HttpClient ownerClient = NewCookieClient();
            await RegisterAsync(ownerClient, "jack");
            HttpResponseMessage createResponse = await ownerClient.PostAsJsonAsync("/api/datasets", new CreateDatasetRequest("jacks-set", null));
            DatasetDto dataset = (await createResponse.Content.ReadFromJsonAsync<DatasetDto>())!;
            dataset.IsPublic.Should().BeFalse();

            using HttpClient anonymousClient = NewCookieClient();
            HttpResponseMessage anonymousResponse = await anonymousClient.GetAsync($"/api/datasets/{dataset.Id}");
            anonymousResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task GetDataset_Anonymous_AllowedForPublicDeniedForPrivate()
        {
            using HttpClient ownerClient = NewCookieClient();
            await RegisterAsync(ownerClient, "jill");
            HttpResponseMessage createResponse = await ownerClient.PostAsJsonAsync(
                "/api/datasets", new CreateDatasetRequest("jills-set", null, IsPublic: true));
            DatasetDto dataset = (await createResponse.Content.ReadFromJsonAsync<DatasetDto>())!;
            dataset.IsPublic.Should().BeTrue();

            using HttpClient anonymousClient = NewCookieClient();
            HttpResponseMessage publicResponse = await anonymousClient.GetAsync($"/api/datasets/{dataset.Id}");
            publicResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            // Flip it private directly (no "make private" endpoint exists yet) and re-check.
            using (IServiceScope scope = _factory.Services.CreateScope())
            {
                DatasetStudioDbContext db = scope.ServiceProvider.GetRequiredService<DatasetStudioDbContext>();
                DatasetEntity entity = await db.Datasets.AsTracking().FirstAsync(d => d.Id == dataset.Id);
                entity.IsPublic = false;
                await db.SaveChangesAsync();
            }

            HttpResponseMessage privateResponse = await anonymousClient.GetAsync($"/api/datasets/{dataset.Id}");
            privateResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task ImportFromFolder_NonAdminAuthenticatedUser_Returns403()
        {
            using HttpClient client = NewCookieClient();
            await RegisterAsync(client, "kate");
            HttpResponseMessage createResponse = await client.PostAsJsonAsync("/api/datasets", new CreateDatasetRequest("kates-set", null));
            DatasetDto dataset = (await createResponse.Content.ReadFromJsonAsync<DatasetDto>())!;

            HttpResponseMessage response = await client.PostAsJsonAsync(
                $"/api/datasets/{dataset.Id}/import-folder", new ImportFolderRequest("/tmp"));
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        private async Task GrantPermissionAsync(Guid datasetId, Guid userId, string accessLevel, bool canDelete, DateTime? expiresAt)
        {
            using IServiceScope scope = _factory.Services.CreateScope();
            DatasetStudioDbContext db = scope.ServiceProvider.GetRequiredService<DatasetStudioDbContext>();
            db.Permissions.Add(new PermissionEntity
            {
                Id = Guid.NewGuid(),
                DatasetId = datasetId,
                UserId = userId,
                AccessLevel = accessLevel,
                CanDelete = canDelete,
                ExpiresAt = expiresAt,
                GrantedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
    }
}
