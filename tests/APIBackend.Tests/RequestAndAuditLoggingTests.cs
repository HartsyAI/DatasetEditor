using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using DatasetStudio.APIBackend.Services.DatasetManagement;
using DatasetStudio.DTO.Auth;
using DatasetStudio.DTO.Datasets;
using DatasetStudio.DTO.Items;
using DatasetStudio.Tests.APIBackend.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DatasetStudio.Tests.APIBackend
{
    /// <summary>
    /// Logging (audit E-5): item edits logged their content but not the editor, the API logged no
    /// requests, logins and registrations logged usernames, and a full import queue left no trace.
    /// </summary>
    [Collection(PostgresCollection.Name)]
    public sealed class RequestAndAuditLoggingTests : IAsyncLifetime
    {
        private readonly PostgresContainerFixture _postgres;
        private readonly RecordingLoggerProvider _logs = new();
        private DatasetStudioApiFactory _factory = default!;

        public RequestAndAuditLoggingTests(PostgresContainerFixture postgres)
        {
            _postgres = postgres;
        }

        public async Task InitializeAsync()
        {
            _factory = new DatasetStudioApiFactory(_postgres.ConnectionString);
            _factory.ConfigureTestServices = services =>
            {
                services.AddSingleton<ILoggerProvider>(_logs);
                services.Configure<IngestionOptions>(options => options.MaxQueuedJobs = 0);
            };
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

        [Fact]
        public async Task ItemEdit_LogsTheEditorAndFields_NotTheContent()
        {
            (HttpClient client, AuthUserDto user) = await SignUpAsync("log-editor");
            DatasetDetailDto dataset = (await (await client.PostAsJsonAsync("/api/datasets", new CreateDatasetRequest("log-edits", null)))
                .Content.ReadFromJsonAsync<DatasetDetailDto>())!;
            Guid itemId = Guid.NewGuid();
            using (IServiceScope scope = _factory.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<IDatasetItemRepository>().AddRangeAsync(dataset.Id, new[]
                {
                    new DatasetItemDto { Id = itemId, DatasetId = dataset.Id, Title = "before", ImageUrl = "https://example.com/a.jpg" }
                });
            }

            HttpResponseMessage edited = await client.PatchAsJsonAsync($"/api/items/{itemId}",
                new UpdateItemRequest { DatasetId = dataset.Id, Title = "Secret caption 7f3a", Tags = new List<string> { "private-tag-9c1" } });
            edited.StatusCode.Should().Be(HttpStatusCode.OK);

            string audit = _logs.Messages.Single(m => m.Contains("[Audit]") && m.Contains(itemId.ToString()));
            audit.Should().Contain(user.Id.ToString()).And.Contain(dataset.Id.ToString()).And.Contain("title,tags");
            _logs.Messages.Should().NotContain(m => m.Contains("Secret caption 7f3a") || m.Contains("private-tag-9c1"));
        }

        [Fact]
        public async Task Requests_AreLoggedByRoute_WithoutQueryOrFileNames()
        {
            (HttpClient client, AuthUserDto user) = await SignUpAsync("log-requests");

            (await client.GetAsync("/api/datasets?search=needle-search-term")).StatusCode.Should().Be(HttpStatusCode.OK);
            Guid missing = Guid.NewGuid();
            (await client.GetAsync($"/api/datasets/{missing}/files/holiday-photo-name.jpg")).StatusCode.Should().Be(HttpStatusCode.NotFound);

            List<string> requests = _logs.ForCategory("DatasetStudio.Requests");
            requests.Should().Contain(m => m.StartsWith("GET /api/datasets/ ") && m.Contains("-> 200") && m.Contains(user.Id.ToString()));
            requests.Should().Contain(m => m.Contains("/files/{*filePath}") && m.Contains(missing.ToString()) && m.Contains("-> 404"));
            _logs.Messages.Should().NotContain(m => m.Contains("needle-search-term") || m.Contains("holiday-photo-name"));
        }

        [Fact]
        public async Task LoginAndRegistration_LogAccountIds_NotNames()
        {
            (_, AuthUserDto user) = await SignUpAsync("log-person-name");
            using HttpClient client = _factory.CreateClient();
            (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("log-person-name", "wrong-password"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("log-person-name", "password123"))).StatusCode.Should().Be(HttpStatusCode.OK);

            _logs.Messages.Should().Contain(m => m.Contains("[Auth] Registered new user " + user.Id));
            _logs.Messages.Should().Contain(m => m.Contains("Failed login for user " + user.Id));
            _logs.Messages.Should().Contain(m => m.Contains("[Auth] User " + user.Id + " logged in"));
            _logs.Messages.Should().NotContain(m => m.Contains("log-person-name"));
        }

        [Fact]
        public async Task FullImportQueue_IsLoggedWithDatasetAndUser()
        {
            (HttpClient client, AuthUserDto user) = await SignUpAsync("log-queue");
            DatasetDetailDto dataset = (await (await client.PostAsJsonAsync("/api/datasets", new CreateDatasetRequest("log-queue", null)))
                .Content.ReadFromJsonAsync<DatasetDetailDto>())!;
            MultipartFormDataContent form = new();
            ByteArrayContent file = new(System.Text.Encoding.UTF8.GetBytes("image_url,title\nhttps://example.com/a.jpg,a\n"));
            form.Add(file, "file", "items.csv");

            (await client.PostAsync($"/api/datasets/{dataset.Id}/upload", form)).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

            _logs.Messages.Should().Contain(m => m.Contains("Queue full") && m.Contains(dataset.Id.ToString()) && m.Contains(user.Id.ToString()));
        }

        /// <summary>Keeps every formatted log message with its category.</summary>
        private sealed class RecordingLoggerProvider : ILoggerProvider
        {
            private readonly ConcurrentQueue<(string Category, string Message)> _entries = new();

            /// <summary>
            /// The app's messages. EF Core's SQL logging is left out: the tests run as Development,
            /// where sensitive data logging (parameter values) is on by design and off elsewhere.
            /// </summary>
            public IEnumerable<string> Messages => _entries
                .Where(e => !e.Category.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
                .Select(e => e.Message).ToList();

            public List<string> ForCategory(string category) => _entries.Where(e => e.Category == category).Select(e => e.Message).ToList();

            public ILogger CreateLogger(string categoryName) => new Recorder(categoryName, _entries);

            public void Dispose()
            {
            }

            private sealed class Recorder : ILogger
            {
                private readonly string _category;
                private readonly ConcurrentQueue<(string Category, string Message)> _entries;

                public Recorder(string category, ConcurrentQueue<(string Category, string Message)> entries)
                {
                    _category = category;
                    _entries = entries;
                }

                public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

                public bool IsEnabled(LogLevel logLevel) => true;

                public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                {
                    _entries.Enqueue((_category, formatter(state, exception)));
                }
            }
        }
    }
}
