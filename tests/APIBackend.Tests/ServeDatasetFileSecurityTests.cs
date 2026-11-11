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
            DatasetDto created = (await createResponse.Content.ReadFromJsonAsync<DatasetDto>())!;

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

        /// <summary>
        /// Audit F-2: the old symlink check resolved only the final file, so a symlinked
        /// directory inside the dataset folder (<c>linked -> /somewhere/else</c>) let
        /// <c>linked/secret.jpg</c> through: the file itself is not a link and the lexical path is
        /// inside the folder. Every path component must be resolved.
        /// </summary>
        [Fact]
        public async Task ServeDatasetFile_SymlinkedDirectoryInsideDatasetFolder_DoesNotLeakItsContents()
        {
            HttpResponseMessage createResponse = await _client.PostAsJsonAsync(
                "/api/datasets", new CreateDatasetRequest("dir-link-test", null));
            DatasetDto created = (await createResponse.Content.ReadFromJsonAsync<DatasetDto>())!;

            string shortId = created.Id.ToString("N")[..8];
            string datasetFolder = Path.Combine(_factory.DatasetRootPath, $"dir-link-test-{shortId}");
            Directory.CreateDirectory(datasetFolder);

            _secretDirectory = Path.Combine(Path.GetTempPath(), "DatasetStudioTests", "secrets", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_secretDirectory);
            await File.WriteAllTextAsync(Path.Combine(_secretDirectory, "secret.jpg"), "TOP-SECRET-CONTENT");

            Directory.CreateSymbolicLink(Path.Combine(datasetFolder, "linked"), _secretDirectory);

            HttpResponseMessage response = await _client.GetAsync($"/api/datasets/{created.Id}/files/linked/secret.jpg");

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            _logs.WarningsContaining(created.Id.ToString(), "linked/secret.jpg").Should().NotBeEmpty();
        }

        /// <summary>
        /// Audit F-1 and F-2 together: an image-named link to a non-image file inside the same
        /// folder passes containment, so the real target's extension is checked as well.
        /// </summary>
        [Fact]
        public async Task ServeDatasetFile_ImageNamedSymlinkToNonImageFile_ReturnsNotFound()
        {
            HttpResponseMessage createResponse = await _client.PostAsJsonAsync(
                "/api/datasets", new CreateDatasetRequest("renamed-link-test", null));
            DatasetDto created = (await createResponse.Content.ReadFromJsonAsync<DatasetDto>())!;

            string shortId = created.Id.ToString("N")[..8];
            string datasetFolder = Path.Combine(_factory.DatasetRootPath, $"renamed-link-test-{shortId}");
            Directory.CreateDirectory(datasetFolder);
            string keyRing = Path.Combine(datasetFolder, "key-ring.xml");
            await File.WriteAllTextAsync(keyRing, "TOP-SECRET-CONTENT");
            File.CreateSymbolicLink(Path.Combine(datasetFolder, "photo.jpg"), keyRing);

            HttpResponseMessage response = await _client.GetAsync($"/api/datasets/{created.Id}/files/photo.jpg");

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        /// <summary>
        /// The F-2 fix compares real paths on both sides, so a dataset folder that is itself reached
        /// through a symlink (a mounted volume, a folder import of a linked path) still serves the
        /// images inside it.
        /// </summary>
        [Fact]
        public async Task ServeDatasetFile_DatasetFolderIsASymlink_StillServesItsImages()
        {
            HttpResponseMessage createResponse = await _client.PostAsJsonAsync(
                "/api/datasets", new CreateDatasetRequest("linked-root-test", null));
            DatasetDto created = (await createResponse.Content.ReadFromJsonAsync<DatasetDto>())!;

            _secretDirectory = Path.Combine(Path.GetTempPath(), "DatasetStudioTests", "linked-roots", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_secretDirectory);
            byte[] imageBytes = { 0xFF, 0xD8, 0xFF, 0xE0, 0x01 };
            await File.WriteAllBytesAsync(Path.Combine(_secretDirectory, "photo.jpg"), imageBytes);

            string shortId = created.Id.ToString("N")[..8];
            Directory.CreateDirectory(_factory.DatasetRootPath);
            Directory.CreateSymbolicLink(Path.Combine(_factory.DatasetRootPath, $"linked-root-test-{shortId}"), _secretDirectory);

            HttpResponseMessage response = await _client.GetAsync($"/api/datasets/{created.Id}/files/photo.jpg");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response.Content.ReadAsByteArrayAsync()).Should().Equal(imageBytes);
        }

        /// <summary>
        /// Audit F-1: only image files are served. A folder-import dataset can point at any
        /// directory, and anything else in it (a DataProtection key ring, a config file) used to be
        /// served as application/octet-stream, world-readable when the dataset is public. The
        /// <c>?w=</c> variant matters too: an undecodable source falls through to the original.
        /// </summary>
        [Theory]
        [InlineData("key-ring.xml", "")]
        [InlineData("notes.txt", "")]
        [InlineData("no-extension", "")]
        [InlineData("key-ring.xml", "?w=100")]
        public async Task ServeDatasetFile_NonImageExtension_ReturnsNotFoundAndLogs(string fileName, string query)
        {
            HttpResponseMessage createResponse = await _client.PostAsJsonAsync(
                "/api/datasets", new CreateDatasetRequest("extension-test", null));
            DatasetDto created = (await createResponse.Content.ReadFromJsonAsync<DatasetDto>())!;

            string shortId = created.Id.ToString("N")[..8];
            string datasetFolder = Path.Combine(_factory.DatasetRootPath, $"extension-test-{shortId}");
            Directory.CreateDirectory(datasetFolder);
            await File.WriteAllTextAsync(Path.Combine(datasetFolder, fileName), "TOP-SECRET-CONTENT");

            HttpResponseMessage response = await _client.GetAsync($"/api/datasets/{created.Id}/files/{fileName}{query}");

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            _logs.WarningsContaining(created.Id.ToString(), fileName).Should().NotBeEmpty();
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
            DatasetDto created = (await createResponse.Content.ReadFromJsonAsync<DatasetDto>())!;

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
