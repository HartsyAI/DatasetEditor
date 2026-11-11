using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using DatasetStudio.APIBackend.Services.Auth;
using DatasetStudio.DTO.Auth;
using DatasetStudio.DTO.Datasets;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using DatasetStudio.APIBackend.DataAccess.PostgreSQL.Repositories;

namespace DatasetStudio.Tests.APIBackend
{
    public sealed class BCryptPasswordHasherTests
    {
        [Fact]
        public void Hash_ThenVerify_RoundTripsCorrectly()
        {
            var hasher = new BCryptPasswordHasher(NullLogger<BCryptPasswordHasher>.Instance);
            string hash = hasher.Hash("correct-horse-battery-staple");

            hasher.Verify("correct-horse-battery-staple", hash).Should().BeTrue();
            hasher.Verify("wrong-password", hash).Should().BeFalse();
        }

        [Fact]
        public void Verify_AgainstMalformedHash_ReturnsFalseInsteadOfThrowing()
        {
            // This is exactly the seeded admin row's shape before AdminBootstrapper repairs it --
            // a literal placeholder string, not real BCrypt output.
            var hasher = new BCryptPasswordHasher(NullLogger<BCryptPasswordHasher>.Instance);

            Action act = () => hasher.Verify("any-password", "$2a$11$placeholder_hash_replace_on_first_run").Should().BeFalse();

            act.Should().NotThrow();
        }
    }

    public sealed class AdminBootstrapperTests
    {
        private static IConfiguration BuildConfig(string? username, string? email, string? password)
        {
            var values = new Dictionary<string, string?>();
            if (username is not null) values["ADMIN_USERNAME"] = username;
            if (email is not null) values["ADMIN_EMAIL"] = email;
            if (password is not null) values["ADMIN_PASSWORD"] = password;
            return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        }

        [Fact]
        public async Task RunAsync_NoAdminPasswordConfigured_SkipsEntirely()
        {
            var userRepo = new Mock<IUserRepository>();
            var bootstrapper = new AdminBootstrapper(userRepo.Object, new BCryptPasswordHasher(NullLogger<BCryptPasswordHasher>.Instance), BuildConfig(null, null, null), NullLogger<AdminBootstrapper>.Instance);

            await bootstrapper.RunAsync(CancellationToken.None);

            userRepo.Verify(r => r.CreateAsync(It.IsAny<UserDto>(), It.IsAny<CancellationToken>()), Times.Never);
            userRepo.Verify(r => r.UpdateAsync(It.IsAny<UserDto>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task RunAsync_SeededPlaceholderRowExists_RepairsItsHashInstepOfSkipping()
        {
            // Regression test for the exact landmine the advisor flagged: an "is users table
            // empty" check would see this row and wrongly conclude bootstrap isn't needed.
            var seededAdmin = new UserDto
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                Username = "admin",
                Email = "admin@localhost",
                PasswordHash = "$2a$11$placeholder_hash_replace_on_first_run",
                Role = "Admin",
                IsActive = true
            };

            var userRepo = new Mock<IUserRepository>();
            userRepo.Setup(r => r.GetByUsernameAsync("admin", It.IsAny<CancellationToken>())).ReturnsAsync(seededAdmin);
            UserDto? updatedWith = null;
            userRepo.Setup(r => r.UpdateAsync(It.IsAny<UserDto>(), It.IsAny<CancellationToken>()))
                .Callback<UserDto, CancellationToken>((u, _) => updatedWith = u)
                .Returns(Task.CompletedTask);

            var hasher = new BCryptPasswordHasher(NullLogger<BCryptPasswordHasher>.Instance);
            var bootstrapper = new AdminBootstrapper(userRepo.Object, hasher, BuildConfig(null, null, "real-password-123"), NullLogger<AdminBootstrapper>.Instance);

            await bootstrapper.RunAsync(CancellationToken.None);

            userRepo.Verify(r => r.CreateAsync(It.IsAny<UserDto>(), It.IsAny<CancellationToken>()), Times.Never);
            updatedWith.Should().NotBeNull();
            updatedWith!.Id.Should().Be(seededAdmin.Id);
            hasher.Verify("real-password-123", updatedWith.PasswordHash).Should().BeTrue();
        }

        [Fact]
        public async Task RunAsync_IsIdempotent_RunningTwiceStillYieldsAWorkingPassword()
        {
            UserDto? stored = null;
            var userRepo = new Mock<IUserRepository>();
            userRepo.Setup(r => r.GetByUsernameAsync("admin", It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => stored);
            userRepo.Setup(r => r.CreateAsync(It.IsAny<UserDto>(), It.IsAny<CancellationToken>()))
                .Callback<UserDto, CancellationToken>((u, _) => stored = u with { Id = Guid.NewGuid() })
                .ReturnsAsync(() => stored!.Id);
            userRepo.Setup(r => r.UpdateAsync(It.IsAny<UserDto>(), It.IsAny<CancellationToken>()))
                .Callback<UserDto, CancellationToken>((u, _) => stored = u)
                .Returns(Task.CompletedTask);

            var hasher = new BCryptPasswordHasher(NullLogger<BCryptPasswordHasher>.Instance);
            var bootstrapper = new AdminBootstrapper(userRepo.Object, hasher, BuildConfig(null, null, "pw-one"), NullLogger<AdminBootstrapper>.Instance);
            await bootstrapper.RunAsync(CancellationToken.None);
            await bootstrapper.RunAsync(CancellationToken.None); // second boot, same env

            stored.Should().NotBeNull();
            hasher.Verify("pw-one", stored!.PasswordHash).Should().BeTrue();
        }

        [Fact]
        public async Task RunAsync_NoExistingUser_CreatesNewAdmin()
        {
            var userRepo = new Mock<IUserRepository>();
            userRepo.Setup(r => r.GetByUsernameAsync("customadmin", It.IsAny<CancellationToken>())).ReturnsAsync((UserDto?)null);
            userRepo.Setup(r => r.CreateAsync(It.IsAny<UserDto>(), It.IsAny<CancellationToken>())).ReturnsAsync(Guid.NewGuid());

            var bootstrapper = new AdminBootstrapper(userRepo.Object, new BCryptPasswordHasher(NullLogger<BCryptPasswordHasher>.Instance), BuildConfig("customadmin", "admin@example.com", "pw"), NullLogger<AdminBootstrapper>.Instance);
            await bootstrapper.RunAsync(CancellationToken.None);

            userRepo.Verify(r => r.CreateAsync(It.Is<UserDto>(u => u.Username == "customadmin" && u.Role == "Admin"), It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    public sealed class DatasetPermissionServiceTests
    {
        private static ClaimsPrincipal AnonymousUser => new(new ClaimsIdentity());

        private static ClaimsPrincipal AuthenticatedUser(Guid userId, string role = "User") => new(new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Role, role)
            }, authenticationType: "Test"));

        private static DatasetRecord PrivateDataset(Guid? ownerId) => new() { Id = Guid.NewGuid(), Name = "d", IsPublic = false, CreatedByUserId = ownerId };
        private static DatasetRecord PublicDataset(Guid? ownerId) => new() { Id = Guid.NewGuid(), Name = "d", IsPublic = true, CreatedByUserId = ownerId };

        [Fact]
        public async Task CanReadAsync_PublicDataset_AllowsAnonymous()
        {
            var service = new DatasetPermissionService(new Mock<IPermissionRepository>().Object);
            DatasetRecord dataset = PublicDataset(Guid.NewGuid());

            (await service.CanReadAsync(dataset, AnonymousUser)).Should().BeTrue();
        }

        [Fact]
        public async Task CanReadAsync_PrivateDataset_DeniesAnonymous()
        {
            var service = new DatasetPermissionService(new Mock<IPermissionRepository>().Object);
            DatasetRecord dataset = PrivateDataset(Guid.NewGuid());

            (await service.CanReadAsync(dataset, AnonymousUser)).Should().BeFalse();
        }

        [Fact]
        public async Task CanReadAsync_PrivateDataset_AllowsOwner()
        {
            Guid ownerId = Guid.NewGuid();
            var service = new DatasetPermissionService(new Mock<IPermissionRepository>().Object);
            DatasetRecord dataset = PrivateDataset(ownerId);

            (await service.CanReadAsync(dataset, AuthenticatedUser(ownerId))).Should().BeTrue();
        }

        [Fact]
        public async Task CanDeleteAsync_SiteAdmin_AllowedEvenWithoutOwnershipOrGrant()
        {
            var permissionRepo = new Mock<IPermissionRepository>();
            permissionRepo.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((PermissionDto?)null);
            var service = new DatasetPermissionService(permissionRepo.Object);
            DatasetRecord dataset = PrivateDataset(Guid.NewGuid());

            (await service.CanDeleteAsync(dataset, AuthenticatedUser(Guid.NewGuid(), role: "Admin"))).Should().BeTrue();
        }

        [Fact]
        public async Task CanWriteAsync_NonOwnerWithNoGrant_Denied()
        {
            var permissionRepo = new Mock<IPermissionRepository>();
            permissionRepo.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((PermissionDto?)null);
            var service = new DatasetPermissionService(permissionRepo.Object);
            DatasetRecord dataset = PrivateDataset(Guid.NewGuid());

            (await service.CanWriteAsync(dataset, AuthenticatedUser(Guid.NewGuid()))).Should().BeFalse();
        }

        [Fact]
        public async Task CanWriteAsync_NonOwnerWithNonExpiredWriteGrant_Allowed()
        {
            Guid granteeId = Guid.NewGuid();
            DatasetRecord dataset = PrivateDataset(Guid.NewGuid());
            var grant = new PermissionDto { DatasetId = dataset.Id, UserId = granteeId, AccessLevel = "Write", ExpiresAt = DateTime.UtcNow.AddDays(1) };

            var permissionRepo = new Mock<IPermissionRepository>();
            permissionRepo.Setup(r => r.GetAsync(dataset.Id, granteeId, It.IsAny<CancellationToken>())).ReturnsAsync(grant);
            var service = new DatasetPermissionService(permissionRepo.Object);

            (await service.CanWriteAsync(dataset, AuthenticatedUser(granteeId))).Should().BeTrue();
        }

        [Fact]
        public async Task CanWriteAsync_ExpiredGrant_Denied()
        {
            Guid granteeId = Guid.NewGuid();
            DatasetRecord dataset = PrivateDataset(Guid.NewGuid());
            var grant = new PermissionDto { DatasetId = dataset.Id, UserId = granteeId, AccessLevel = "Write", ExpiresAt = DateTime.UtcNow.AddDays(-1) };

            var permissionRepo = new Mock<IPermissionRepository>();
            permissionRepo.Setup(r => r.GetAsync(dataset.Id, granteeId, It.IsAny<CancellationToken>())).ReturnsAsync(grant);
            var service = new DatasetPermissionService(permissionRepo.Object);

            (await service.CanWriteAsync(dataset, AuthenticatedUser(granteeId))).Should().BeFalse();
        }

        [Fact]
        public async Task CanDeleteAsync_ReadOnlyGrantWithoutCanDeleteFlag_Denied()
        {
            Guid granteeId = Guid.NewGuid();
            DatasetRecord dataset = PrivateDataset(Guid.NewGuid());
            // A grant can allow read/write without allowing delete -- CanDelete is its own flag.
            var grant = new PermissionDto { DatasetId = dataset.Id, UserId = granteeId, AccessLevel = "Write", CanDelete = false };

            var permissionRepo = new Mock<IPermissionRepository>();
            permissionRepo.Setup(r => r.GetAsync(dataset.Id, granteeId, It.IsAny<CancellationToken>())).ReturnsAsync(grant);
            var service = new DatasetPermissionService(permissionRepo.Object);

            (await service.CanDeleteAsync(dataset, AuthenticatedUser(granteeId))).Should().BeFalse();
        }
    }
}
