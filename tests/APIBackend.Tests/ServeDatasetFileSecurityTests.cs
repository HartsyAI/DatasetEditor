using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using DatasetStudio.DTO.Datasets;
using DatasetStudio.Tests.APIBackend.Fixtures;
using FluentAssertions;
using Xunit;

namespace DatasetStudio.Tests.APIBackend
{
    // Regression test for the ServeDatasetFile symlink-following bug: the original
    // containment check only validated the requested path itself (Path.GetFullPath + a
    // prefix check), which a symlink placed directly inside a dataset's own folder trivially
    // satisfies — File.OpenRead then transparently follows the link and serves whatever it
    // points at, anywhere on disk. The fix resolves the link chain and re-checks containment
    // against the real target.
    [Collection(PostgresCollection.Name)]
    public sealed class ServeDatasetFileSecurityTests : IAsyncLifetime
    {
        private readonly PostgresContainerFixture _postgres;
        private DatasetStudioApiFactory _factory = default!;
        private HttpClient _client = default!;
        private string? _secretDirectory;

        public ServeDatasetFileSecurityTests(PostgresContainerFixture postgres)
        {
            _postgres = postgres;
        }

        public async Task InitializeAsync()
        {
            _factory = new DatasetStudioApiFactory(_postgres.ConnectionString);
            await _factory.InitializeDatabaseAsync();
            _client = _factory.CreateClient();

            // CreateDataset requires authentication as of Phase 3.
            await _client.PostAsJsonAsync("/api/auth/register",
                new DatasetStudio.DTO.Auth.RegisterRequest("serve-file-security-test-user", "serve-file-security-test-user@example.com", "password123"));
        }

        public async Task DisposeAsync()
        {
            _client.Dispose();
            await _factory.DropDatabaseAsync();
            await _factory.DisposeAsync();
            if (_secretDirectory is not null && Directory.Exists(_secretDirectory))
            {
                Directory.Delete(_secretDirectory, recursive: true);
            }
        }

        [Fact]
        public async Task ServeDatasetFile_SymlinkInsideDatasetFolder_DoesNotLeakItsTarget()
        {
            // "traversal-test" is already Slugify-safe (lowercase, alnum + hyphens), so the
            // server computes its folder name the same way this test does: {slug}-{first 8
            // hex chars of the id, "N" format} — see DatasetEndpoints.GetDatasetFolderPathForFile.
            HttpResponseMessage createResponse = await _client.PostAsJsonAsync(
                "/api/datasets", new CreateDatasetRequest("traversal-test", null));
            DatasetDetailDto created = (await createResponse.Content.ReadFromJsonAsync<DatasetDetailDto>())!;

            string shortId = created.Id.ToString("N")[..8];
            string datasetFolder = Path.Combine(_factory.DatasetRootPath, $"traversal-test-{shortId}");
            Directory.CreateDirectory(datasetFolder);

            _secretDirectory = Path.Combine(Path.GetTempPath(), "DatasetStudioTests", "secrets", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_secretDirectory);
            string secretFilePath = Path.Combine(_secretDirectory, "secret.jpg");
            await File.WriteAllTextAsync(secretFilePath, "TOP-SECRET-CONTENT");

            string symlinkPath = Path.Combine(datasetFolder, "sneaky.jpg");
            File.CreateSymbolicLink(symlinkPath, secretFilePath);

            HttpResponseMessage response = await _client.GetAsync($"/api/datasets/{created.Id}/files/sneaky.jpg");

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }
}
