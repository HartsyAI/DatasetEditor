using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using DatasetStudio.DTO.Datasets;
using DatasetStudio.Tests.APIBackend.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
        private readonly CapturingLoggerProvider _logs = new();

        public ServeDatasetFileSecurityTests(PostgresContainerFixture postgres)
        {
            _postgres = postgres;
        }

        public async Task InitializeAsync()
        {
            _factory = new DatasetStudioApiFactory(_postgres.ConnectionString);
            _factory.ConfigureTestServices = services => services.AddSingleton<ILoggerProvider>(_logs);
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
            // Audit F-4: the refusal is a security event and must be logged, not just 404'd.
            _logs.WarningsContaining(created.Id.ToString(), "sneaky.jpg").Should().NotBeEmpty();
        }

        // Phase 4 folded the thumbnail resize branch into this same handler specifically so it
        // would inherit the containment/symlink checks above as-is, not a second copy of them.
        // This proves that actually holds: the resize branch runs strictly after those checks,
        // not before, so a ?w= request against a symlink escape must 404 exactly like a plain one.
        [Fact]
        public async Task ServeDatasetFile_SymlinkInsideDatasetFolder_WithWidthQuery_DoesNotLeakItsTarget()
        {
            HttpResponseMessage createResponse = await _client.PostAsJsonAsync(
                "/api/datasets", new CreateDatasetRequest("traversal-test-thumb", null));
            DatasetDetailDto created = (await createResponse.Content.ReadFromJsonAsync<DatasetDetailDto>())!;

            string shortId = created.Id.ToString("N")[..8];
            string datasetFolder = Path.Combine(_factory.DatasetRootPath, $"traversal-test-thumb-{shortId}");
            Directory.CreateDirectory(datasetFolder);

            _secretDirectory = Path.Combine(Path.GetTempPath(), "DatasetStudioTests", "secrets", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_secretDirectory);
            string secretFilePath = Path.Combine(_secretDirectory, "secret.jpg");
            await File.WriteAllTextAsync(secretFilePath, "TOP-SECRET-CONTENT");

            string symlinkPath = Path.Combine(datasetFolder, "sneaky.jpg");
            File.CreateSymbolicLink(symlinkPath, secretFilePath);

            HttpResponseMessage response = await _client.GetAsync($"/api/datasets/{created.Id}/files/sneaky.jpg?w=400");

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }
}
