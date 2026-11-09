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

        private async Task<DatasetStudioApiFactory> NewFactoryAsync(Action<AuthOptions>? configure = null)
        {
            DatasetStudioApiFactory factory = new(_postgres.ConnectionString);
            if (configure is not null)
            {
                factory.ConfigureTestServices = services => services.Configure(configure);
            }
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
