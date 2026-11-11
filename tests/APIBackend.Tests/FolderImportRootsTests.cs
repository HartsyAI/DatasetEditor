using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using DatasetStudio.APIBackend.DataAccess.PostgreSQL;
using DatasetStudio.APIBackend.DataAccess.PostgreSQL.Entities;
using DatasetStudio.DTO.Datasets;
using DatasetStudio.Tests.APIBackend.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DatasetStudio.Tests.APIBackend
{
    /// <summary>
    /// Audit F-1: server-folder import could point a dataset at any directory the API process can
    /// read (<c>/etc</c>, the DataProtection key ring under <c>data/keys</c>). It is now limited to
    /// the folders listed in <c>Storage:FolderImportRoots</c>, compared by real path, and refused
    /// outright when none are configured.
    /// </summary>
    [Collection(PostgresCollection.Name)]
    public sealed class FolderImportRootsTests : IAsyncLifetime
    {
        private readonly PostgresContainerFixture _postgres;
        private readonly List<DatasetStudioApiFactory> _factories = new();
        private readonly List<string> _folders = new();
        private readonly string _root = Path.Combine(Path.GetTempPath(), "DatasetStudioTests", "import-roots", Guid.NewGuid().ToString("N"));

        public FolderImportRootsTests(PostgresContainerFixture postgres)
        {
            _postgres = postgres;
        }

        public Task InitializeAsync()
        {
            Directory.CreateDirectory(_root);
            _folders.Add(_root);
            return Task.CompletedTask;
        }

        public async Task DisposeAsync()
        {
            foreach (DatasetStudioApiFactory factory in _factories)
            {
                // Stop the host first: an accepted import leaves the ingestion worker using the
                // database, and dropping it underneath fails with "being accessed by other users".
                await factory.DisposeAsync();
                await factory.DropDatabaseAsync();
            }
            foreach (string folder in _folders)
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
        }

        /// <summary>Starts a host with the given import roots and returns a client signed in as an admin.</summary>
        private async Task<HttpClient> CreateAdminClientAsync(params string[] roots)
        {
            DatasetStudioApiFactory factory = new(_postgres.ConnectionString) { FolderImportRoots = roots };
            _factories.Add(factory);
            await factory.InitializeDatabaseAsync();
            HttpClient client = factory.CreateClient();

            await client.PostAsJsonAsync("/api/auth/register",
                new DatasetStudio.DTO.Auth.RegisterRequest("roots-admin", "roots-admin@example.com", "password123"));
            using (IServiceScope scope = factory.Services.CreateScope())
            {
                DatasetStudioDbContext db = scope.ServiceProvider.GetRequiredService<DatasetStudioDbContext>();
                UserEntity user = await db.Users.AsTracking().FirstAsync(u => u.Username == "roots-admin");
                user.Role = "Admin";
                await db.SaveChangesAsync();
            }
            // The role claim is baked into the cookie at registration; log in again to pick it up.
            await client.PostAsJsonAsync("/api/auth/login", new DatasetStudio.DTO.Auth.LoginRequest("roots-admin", "password123"));
            return client;
        }

        private static async Task<HttpResponseMessage> ImportAsync(HttpClient client, string folder)
        {
            HttpResponseMessage createResponse = await client.PostAsJsonAsync(
                "/api/datasets", new CreateDatasetRequest($"roots-{Guid.NewGuid():N}"[..20], null));
            DatasetDto created = (await createResponse.Content.ReadFromJsonAsync<DatasetDto>())!;
            return await client.PostAsJsonAsync($"/api/datasets/{created.Id}/import-folder", new ImportFolderRequest(folder));
        }

        private string MakeOutsideFolder()
        {
            // Outside the shared DatasetStudioTests tree on purpose: that tree is the default root.
            string outside = Path.Combine(Path.GetTempPath(), "DatasetStudioOutsideRoots", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outside);
            _folders.Add(outside);
            return outside;
        }

        [Fact]
        public async Task ImportFromFolder_NoRootsConfigured_IsRefusedWithAClearMessage()
        {
            HttpClient client = await CreateAdminClientAsync();
            string folder = Path.Combine(_root, "photos");
            Directory.CreateDirectory(folder);

            HttpResponseMessage response = await ImportAsync(client, folder);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await response.Content.ReadAsStringAsync()).Should().Contain("Storage:FolderImportRoots");
        }

        [Fact]
        public async Task ImportFromFolder_FolderOutsideTheRoots_IsRefused()
        {
            HttpClient client = await CreateAdminClientAsync(_root);

            HttpResponseMessage response = await ImportAsync(client, MakeOutsideFolder());

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await response.Content.ReadAsStringAsync()).Should().Contain("outside");
        }

        [Fact]
        public async Task ImportFromFolder_SymlinkInsideARootPointingOutside_IsRefused()
        {
            HttpClient client = await CreateAdminClientAsync(_root);
            string link = Path.Combine(_root, "looks-inside");
            Directory.CreateSymbolicLink(link, MakeOutsideFolder());

            HttpResponseMessage response = await ImportAsync(client, link);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task ImportFromFolder_FolderInsideARoot_IsAccepted()
        {
            HttpClient client = await CreateAdminClientAsync(_root);
            string folder = Path.Combine(_root, "nested", "photos");
            Directory.CreateDirectory(folder);

            HttpResponseMessage response = await ImportAsync(client, folder);

            response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        }
    }
}
