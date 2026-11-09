using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using DatasetStudio.APIBackend.DataAccess.PostgreSQL;
using DatasetStudio.APIBackend.Services.Auth;
using DatasetStudio.Core.Abstractions.Repositories;
using DatasetStudio.DTO.Auth;
using DatasetStudio.DTO.Datasets;
using DatasetStudio.Tests.APIBackend.Fixtures;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DatasetStudio.Tests.APIBackend
{
    /// <summary>
    /// Account hardening (audit A-1 to A-7): the registration switch, per-IP limits on login and
    /// registration, sessions re-checked against the account, case-insensitive uniqueness enforced
    /// by the database, input validation, and the admin bootstrap no longer rewriting the hash.
    /// </summary>
    [Collection(PostgresCollection.Name)]
    public sealed class AccountHardeningTests : IAsyncLifetime
    {
        private readonly PostgresContainerFixture _postgres;
        private readonly List<DatasetStudioApiFactory> _factories = new();

        public AccountHardeningTests(PostgresContainerFixture postgres)
        {
            _postgres = postgres;
        }

        public Task InitializeAsync() => Task.CompletedTask;

        public async Task DisposeAsync()
        {
            foreach (DatasetStudioApiFactory factory in _factories)
            {
                await factory.DropDatabaseAsync();
                await factory.DisposeAsync();
            }
        }

        private async Task<DatasetStudioApiFactory> NewFactoryAsync(Action<AuthOptions>? configure = null, Action<IServiceCollection>? services = null)
        {
            DatasetStudioApiFactory factory = new(_postgres.ConnectionString);
            factory.ConfigureTestServices = collection =>
            {
                if (configure is not null)
                {
                    collection.Configure(configure);
                }
                services?.Invoke(collection);
            };
            await factory.InitializeDatabaseAsync();
            _factories.Add(factory);
            return factory;
        }

        private static Task<HttpResponseMessage> RegisterAsync(HttpClient client, string username, string? email = null, string password = "password123") =>
            client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(username, email ?? $"{username}@example.com", password));

        private static async Task<string?> ErrorOfAsync(HttpResponseMessage response)
        {
            Dictionary<string, string>? body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
            return body?.GetValueOrDefault("error");
        }

        private static async Task UpdateUserAsync(DatasetStudioApiFactory factory, Guid userId, Action<global::DatasetStudio.APIBackend.DataAccess.PostgreSQL.Entities.UserEntity> change)
        {
            using IServiceScope scope = factory.Services.CreateScope();
            DatasetStudioDbContext db = scope.ServiceProvider.GetRequiredService<DatasetStudioDbContext>();
            global::DatasetStudio.APIBackend.DataAccess.PostgreSQL.Entities.UserEntity entity = await db.Users.SingleAsync(u => u.Id == userId);
            change(entity);
            // The context is no-tracking by default, so mark the change explicitly.
            db.Users.Update(entity);
            await db.SaveChangesAsync();
        }

        [Fact]
        public async Task Register_WhenRegistrationIsClosed_Returns403AndSettingsSaySo()
        {
            DatasetStudioApiFactory factory = await NewFactoryAsync(o => o.AllowRegistration = false);
            using HttpClient client = factory.CreateClient();

            AuthSettingsDto? settings = await client.GetFromJsonAsync<AuthSettingsDto>("/api/auth/settings");
            settings!.AllowRegistration.Should().BeFalse();

            HttpResponseMessage response = await RegisterAsync(client, "closedreg");
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await ErrorOfAsync(response)).Should().Contain("Registration is closed");

            using IServiceScope scope = factory.Services.CreateScope();
            IUserRepository users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            (await users.GetByUsernameAsync("closedreg")).Should().BeNull();
        }

        [Fact]
        public void Registration_IsClosedByDefault_OutsideDevelopment()
        {
            new AuthOptions().AllowRegistration.Should().BeFalse();
        }

        [Fact]
        public async Task Login_BeyondThePerIpLimit_Returns429WithRetryAfter()
        {
            DatasetStudioApiFactory factory = await NewFactoryAsync(o => o.LoginAttemptsPerMinute = 3);
            using HttpClient client = factory.CreateClient();

            for (int attempt = 0; attempt < 3; attempt++)
            {
                HttpResponseMessage allowed = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("nobody", "wrong-password"));
                allowed.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            }

            HttpResponseMessage limited = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("nobody", "wrong-password"));
            limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
            limited.Headers.RetryAfter.Should().NotBeNull();
            (await ErrorOfAsync(limited)).Should().Contain("Too many attempts");
        }

        /// <summary>
        /// PR 5.1 review: X-Forwarded-For was believed from any private-network address, so a LAN
        /// client reaching the API's published port directly could send a new address with each
        /// login and never hit the limit. With no proxy configured it is ignored.
        /// </summary>
        [Fact]
        public async Task Login_FromAnUntrustedPeer_CantDodgeTheLimitWithForwardedFor()
        {
            DatasetStudioApiFactory factory = await NewFactoryAsync(o => o.LoginAttemptsPerMinute = 3,
                services => services.AddTransient<Microsoft.AspNetCore.Hosting.IStartupFilter>(_ => new PeerAddressStartupFilter("10.1.2.3")));
            using HttpClient client = factory.CreateClient();

            HttpStatusCode[] statuses = await LoginWithForwardedForAsync(client, 4);

            statuses.Take(3).Should().AllBeEquivalentTo(HttpStatusCode.Unauthorized);
            statuses[3].Should().Be(HttpStatusCode.TooManyRequests);
        }

        /// <summary>Behind a configured proxy, each forwarded client address gets its own allowance.</summary>
        [Fact]
        public async Task Login_ThroughAConfiguredProxy_LimitsEachForwardedClientSeparately()
        {
            IConfiguration proxyConfig = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["ReverseProxy:KnownProxies:0"] = "10.1.2.3" })
                .Build();
            DatasetStudioApiFactory factory = await NewFactoryAsync(o => o.LoginAttemptsPerMinute = 3, services =>
            {
                services.AddTransient<Microsoft.AspNetCore.Hosting.IStartupFilter>(_ => new PeerAddressStartupFilter("10.1.2.3"));
                services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(o =>
                    global::DatasetStudio.APIBackend.Configuration.ReverseProxySettings.Apply(o, proxyConfig));
            });
            using HttpClient client = factory.CreateClient();

            HttpStatusCode[] statuses = await LoginWithForwardedForAsync(client, 4);

            statuses.Should().AllBeEquivalentTo(HttpStatusCode.Unauthorized);
        }

        [Theory]
        [InlineData("ReverseProxy:KnownProxies:0", "caddy")]
        [InlineData("ReverseProxy:KnownNetworks:0", "10.0.0.0/33")]
        public void ReverseProxySettings_RejectsEntriesThatDontParse(string key, string value)
        {
            IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [key] = value }).Build();
            Action apply = () => global::DatasetStudio.APIBackend.Configuration.ReverseProxySettings.Apply(new Microsoft.AspNetCore.Builder.ForwardedHeadersOptions(), config);
            apply.Should().Throw<InvalidOperationException>().WithMessage($"*{value}*");
        }

        private static async Task<HttpStatusCode[]> LoginWithForwardedForAsync(HttpClient client, int attempts)
        {
            List<HttpStatusCode> statuses = new();
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                using HttpRequestMessage request = new(HttpMethod.Post, "/api/auth/login")
                {
                    Content = JsonContent.Create(new LoginRequest("nobody", "wrong-password"))
                };
                request.Headers.Add("X-Forwarded-For", $"203.0.113.{attempt + 1}");
                using HttpResponseMessage response = await client.SendAsync(request);
                statuses.Add(response.StatusCode);
            }
            return statuses.ToArray();
        }

        /// <summary>The in-memory test server has no peer address; this gives requests one.</summary>
        private sealed class PeerAddressStartupFilter : Microsoft.AspNetCore.Hosting.IStartupFilter
        {
            private readonly IPAddress _address;

            public PeerAddressStartupFilter(string address)
            {
                _address = IPAddress.Parse(address);
            }

            public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next) => app =>
            {
                app.Use((context, nextMiddleware) =>
                {
                    context.Connection.RemoteIpAddress = _address;
                    return nextMiddleware(context);
                });
                next(app);
            };
        }

        /// <summary>
        /// PR 5.1 review: renaming case-only duplicates with "-" plus the id's first 8 characters
        /// could produce a name another account already had (here "alice-20000000"), and the
        /// unique-index creation then failed the migration and the API's startup. Names are now
        /// checked against the table as it stands, including ones assigned in the same run.
        /// </summary>
        [Fact]
        public async Task Migration_RenamesCaseOnlyDuplicates_WithoutCollidingWithExistingNames()
        {
            string databaseName = $"dataset_studio_dupes_{Guid.NewGuid():N}";
            Npgsql.NpgsqlConnectionStringBuilder admin = new(_postgres.ConnectionString) { Database = "postgres", Pooling = false };
            Npgsql.NpgsqlConnectionStringBuilder target = new(_postgres.ConnectionString) { Database = databaseName, Pooling = false };
            await using (Npgsql.NpgsqlConnection connection = new(admin.ConnectionString))
            {
                await connection.OpenAsync();
                await using Npgsql.NpgsqlCommand create = new($"CREATE DATABASE \"{databaseName}\"", connection);
                await create.ExecuteNonQueryAsync();
            }

            try
            {
                DbContextOptions<DatasetStudioDbContext> options = new DbContextOptionsBuilder<DatasetStudioDbContext>()
                    .UseNpgsql(target.ConnectionString).Options;
                await using (DatasetStudioDbContext db = new(options))
                {
                    Microsoft.EntityFrameworkCore.Migrations.IMigrator migrator =
                        Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(db);
                    await migrator.MigrateAsync("20261109185739_AddDatasetRowVersion");
                    await db.Database.ExecuteSqlRawAsync(@"
INSERT INTO users (id, username, email, password_hash, role, is_active, email_verified, created_at) VALUES
 ('10000000-0000-0000-0000-000000000001', 'alice',          'alice@example.com',      'x', 'User', true, false, '2024-01-01'),
 ('20000000-0000-0000-0000-000000000002', 'Alice',          'ALICE@example.com',      'x', 'User', true, false, '2024-02-01'),
 ('30000000-0000-0000-0000-000000000003', 'alice-1',        'dup1-alice@example.com', 'x', 'User', true, false, '2024-03-01'),
 ('40000000-0000-0000-0000-000000000004', 'ALICE',          'Alice@Example.com',      'x', 'User', true, false, '2024-04-01'),
 ('50000000-0000-0000-0000-000000000005', 'alice-20000000', 'other@example.com',      'x', 'User', true, false, '2024-05-01')");

                    await db.Database.MigrateAsync();
                }

                Dictionary<string, (string Username, string Email)> rows = new();
                await using (Npgsql.NpgsqlConnection connection = new(target.ConnectionString))
                {
                    await connection.OpenAsync();
                    await using Npgsql.NpgsqlCommand select = new("SELECT id::text, username, email FROM users WHERE username <> 'admin'", connection);
                    await using Npgsql.NpgsqlDataReader reader = await select.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        rows[reader.GetString(0)[..1]] = (reader.GetString(1), reader.GetString(2));
                    }
                }

                rows["1"].Should().Be(("alice", "alice@example.com"));
                rows["2"].Should().Be(("Alice-2", "dup2-ALICE@example.com"));
                rows["3"].Should().Be(("alice-1", "dup1-alice@example.com"));
                rows["4"].Should().Be(("ALICE-3", "dup3-Alice@Example.com"));
                rows["5"].Should().Be(("alice-20000000", "other@example.com"));
            }
            finally
            {
                Npgsql.NpgsqlConnection.ClearAllPools();
                await using Npgsql.NpgsqlConnection connection = new(admin.ConnectionString);
                await connection.OpenAsync();
                await using Npgsql.NpgsqlCommand drop = new($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", connection);
                await drop.ExecuteNonQueryAsync();
            }
        }

        [Fact]
        public async Task Register_BeyondThePerIpLimit_Returns429()
        {
            DatasetStudioApiFactory factory = await NewFactoryAsync(o => o.RegistrationsPerHour = 2);
            using HttpClient client = factory.CreateClient();

            (await RegisterAsync(client, "limit1")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await RegisterAsync(client, "limit2")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await RegisterAsync(client, "limit3")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        }

        [Fact]
        public async Task Session_OfADeactivatedAccount_IsRejected()
        {
            DatasetStudioApiFactory factory = await NewFactoryAsync(o => o.RevalidateEvery = TimeSpan.Zero);
            using HttpClient client = factory.CreateClient();
            AuthUserDto user = (await (await RegisterAsync(client, "deactivated")).Content.ReadFromJsonAsync<AuthUserDto>())!;
            (await client.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);

            await UpdateUserAsync(factory, user.Id, u => u.IsActive = false);

            (await client.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task Session_AfterTheSecurityStampChanges_IsRejected()
        {
            DatasetStudioApiFactory factory = await NewFactoryAsync(o => o.RevalidateEvery = TimeSpan.Zero);
            using HttpClient client = factory.CreateClient();
            AuthUserDto user = (await (await RegisterAsync(client, "stamped")).Content.ReadFromJsonAsync<AuthUserDto>())!;

            await UpdateUserAsync(factory, user.Id, u => u.SecurityStamp = "ROTATED");

            (await client.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task Session_PicksUpARoleChange_WithoutSigningInAgain()
        {
            DatasetStudioApiFactory factory = await NewFactoryAsync(o => o.RevalidateEvery = TimeSpan.Zero);
            using HttpClient client = factory.CreateClient();
            AuthUserDto user = (await (await RegisterAsync(client, "promoted")).Content.ReadFromJsonAsync<AuthUserDto>())!;
            user.Role.Should().Be("User");

            await UpdateUserAsync(factory, user.Id, u => u.Role = "Admin");

            AuthUserDto? me = await client.GetFromJsonAsync<AuthUserDto>("/api/auth/me");
            me!.Role.Should().Be("Admin");
        }

        [Fact]
        public async Task Session_WithTheDefaultInterval_StillWorksAfterSigningInAgainFollowingARoleChange()
        {
            // The cached account state must not override a fresh cookie: sign in, get cached as
            // "User", get promoted, sign in again, and the new session is an admin at once.
            DatasetStudioApiFactory factory = await NewFactoryAsync();
            using HttpClient client = factory.CreateClient();
            AuthUserDto user = (await (await RegisterAsync(client, "relogin")).Content.ReadFromJsonAsync<AuthUserDto>())!;
            (await client.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);

            await UpdateUserAsync(factory, user.Id, u => u.Role = "Admin");
            (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("relogin", "password123"))).StatusCode.Should().Be(HttpStatusCode.OK);

            AuthUserDto? me = await client.GetFromJsonAsync<AuthUserDto>("/api/auth/me");
            me!.Role.Should().Be("Admin");
        }

        [Fact]
        public async Task Database_RejectsAUsernameOrEmailThatDiffersOnlyInCase()
        {
            DatasetStudioApiFactory factory = await NewFactoryAsync();
            using IServiceScope scope = factory.Services.CreateScope();
            IUserRepository users = scope.ServiceProvider.GetRequiredService<IUserRepository>();

            await users.CreateAsync(new UserDto { Id = Guid.NewGuid(), Username = "casey", Email = "casey@example.com", PasswordHash = "x" });

            Func<Task> sameNameDifferentCase = () => users.CreateAsync(new UserDto { Id = Guid.NewGuid(), Username = "CASEY", Email = "other@example.com", PasswordHash = "x" });
            await sameNameDifferentCase.Should().ThrowAsync<DuplicateUserException>();

            using IServiceScope scope2 = factory.Services.CreateScope();
            IUserRepository users2 = scope2.ServiceProvider.GetRequiredService<IUserRepository>();
            Func<Task> sameEmailDifferentCase = () => users2.CreateAsync(new UserDto { Id = Guid.NewGuid(), Username = "casey2", Email = "Casey@Example.com", PasswordHash = "x" });
            await sameEmailDifferentCase.Should().ThrowAsync<DuplicateUserException>();
        }

        [Fact]
        public async Task SimultaneousRegistrations_OfTheSameName_CreateOneAccountAndNo500s()
        {
            DatasetStudioApiFactory factory = await NewFactoryAsync();
            HttpResponseMessage[] responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            {
                HttpClient client = factory.CreateClient();
                return RegisterAsync(client, i % 2 == 0 ? "racer" : "RACER", $"racer{i}@example.com");
            }));

            responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
            responses.Where(r => r.StatusCode != HttpStatusCode.OK).Should().OnlyContain(r => r.StatusCode == HttpStatusCode.BadRequest);
        }

        [Theory]
        [InlineData("ab", "short@example.com", "password123", "Usernames are 3 to 50")]
        [InlineData("has space", "space@example.com", "password123", "Usernames are 3 to 50")]
        [InlineData("at@sign", "at@example.com", "password123", "Usernames are 3 to 50")]
        [InlineData("waytoolongusernamewaytoolongusernamewaytoolongusername", "long@example.com", "password123", "Usernames are 3 to 50")]
        [InlineData("bademail", "not-an-email", "password123", "valid email")]
        [InlineData("shortpw", "shortpw@example.com", "short", "at least 8")]
        public async Task Register_WithInvalidInput_Returns400WithAMessage(string username, string email, string password, string expected)
        {
            DatasetStudioApiFactory factory = await NewFactoryAsync();
            using HttpClient client = factory.CreateClient();

            HttpResponseMessage response = await RegisterAsync(client, username, email, password);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await ErrorOfAsync(response)).Should().Contain(expected);
        }

        [Fact]
        public async Task Register_WithAnOverlongEmailOrPassword_Returns400InsteadOf500()
        {
            DatasetStudioApiFactory factory = await NewFactoryAsync();
            using HttpClient client = factory.CreateClient();

            HttpResponseMessage longEmail = await RegisterAsync(client, "longemail", new string('e', 250) + "@example.com");
            longEmail.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await ErrorOfAsync(longEmail)).Should().Contain("valid email");

            HttpResponseMessage longPassword = await RegisterAsync(client, "longpassword", password: new string('p', 73));
            longPassword.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await ErrorOfAsync(longPassword)).Should().Contain("at most 72 bytes");

            HttpResponseMessage longName = await client.PostAsJsonAsync("/api/auth/register",
                new RegisterRequest("longdisplay", "longdisplay@example.com", "password123", new string('d', 101)));
            longName.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task AdminBootstrap_KeepsTheHashWhenThePasswordIsUnchanged_AndRotatesTheStampWhenItChanges()
        {
            DatasetStudioApiFactory factory = await NewFactoryAsync();
            using IServiceScope scope = factory.Services.CreateScope();
            IUserRepository users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            IPasswordHasher hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

            AdminBootstrapper Bootstrapper(string password) => new(users, hasher,
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ADMIN_USERNAME"] = "root",
                    ["ADMIN_EMAIL"] = "root@example.com",
                    ["ADMIN_PASSWORD"] = password
                }).Build(),
                NullLogger<AdminBootstrapper>.Instance);

            await Bootstrapper("first-password").RunAsync();
            UserDto first = (await users.GetByUsernameAsync("root"))!;

            await Bootstrapper("first-password").RunAsync();
            UserDto second = (await users.GetByUsernameAsync("root"))!;
            second.PasswordHash.Should().Be(first.PasswordHash);
            second.SecurityStamp.Should().Be(first.SecurityStamp);

            await Bootstrapper("second-password").RunAsync();
            UserDto third = (await users.GetByUsernameAsync("root"))!;
            hasher.Verify("second-password", third.PasswordHash).Should().BeTrue();
            third.SecurityStamp.Should().NotBe(first.SecurityStamp);
        }

        [Fact]
        public async Task AdminBootstrap_WithAnotherUsername_DeactivatesTheSeededPlaceholderAdmin()
        {
            DatasetStudioApiFactory factory = await NewFactoryAsync();
            using IServiceScope scope = factory.Services.CreateScope();
            IUserRepository users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            IPasswordHasher hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

            AdminBootstrapper bootstrapper = new(users, hasher,
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ADMIN_USERNAME"] = "owner",
                    ["ADMIN_PASSWORD"] = "owner-password"
                }).Build(),
                NullLogger<AdminBootstrapper>.Instance);
            await bootstrapper.RunAsync();

            UserDto? seeded = await users.GetByIdAsync(Guid.Parse("00000000-0000-0000-0000-000000000001"));
            seeded!.IsActive.Should().BeFalse();
            seeded.Username.Should().NotBe("admin");
            UserDto owner = (await users.GetByUsernameAsync("owner"))!;
            owner.IsActive.Should().BeTrue();
            owner.Email.Should().Be("admin@localhost", "the default ADMIN_EMAIL no longer collides with the seeded row");
            (await users.GetByUsernameAsync("admin")).Should().BeNull();

            // A second boot changes nothing.
            await bootstrapper.RunAsync();
            (await users.GetByUsernameAsync("owner"))!.PasswordHash.Should().Be(owner.PasswordHash);
        }

        [Fact]
        public void DatasetDetail_IsPrivateByDefault_LikeTheOtherDatasetContracts()
        {
            new DatasetDetailDto().IsPublic.Should().BeFalse();
            new DatasetDto().IsPublic.Should().BeFalse();
        }
    }
}
