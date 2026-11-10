using System.Collections.Generic;
using DatasetStudio.APIBackend.Services.Storage;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DatasetStudio.Tests.APIBackend
{
    public sealed class S3StorageProfileResolverTests
    {
        private static S3StorageProfileResolver CreateResolver() => new(new[]
        {
            new S3StorageProfileConfig
            {
                Name = "minio-dev",
                ServiceUrl = "http://localhost:9000",
                AccessKey = "minioadmin",
                SecretKey = "minioadmin",
                AllowedBuckets = new List<string> { "dataset-bucket", "other-bucket" },
            },
        });

        [Fact]
        public void GetProfile_KnownName_ReturnsProfile()
        {
            S3StorageProfileResolver resolver = CreateResolver();

            S3StorageProfileConfig? profile = resolver.GetProfile("minio-dev");

            profile.Should().NotBeNull();
            profile!.ServiceUrl.Should().Be("http://localhost:9000");
        }

        [Fact]
        public void GetProfile_NameIsCaseInsensitive()
        {
            S3StorageProfileResolver resolver = CreateResolver();

            resolver.GetProfile("MINIO-DEV").Should().NotBeNull();
        }

        [Fact]
        public void GetProfile_UnknownName_ReturnsNull()
        {
            S3StorageProfileResolver resolver = CreateResolver();

            resolver.GetProfile("does-not-exist").Should().BeNull();
        }

        [Fact]
        public void IsBucketAllowed_BucketInAllowList_ReturnsTrue()
        {
            S3StorageProfileResolver resolver = CreateResolver();

            resolver.IsBucketAllowed("minio-dev", "dataset-bucket").Should().BeTrue();
        }

        [Fact]
        public void IsBucketAllowed_BucketNotInAllowList_ReturnsFalse()
        {
            // The core security property this class exists for: a profile's credentials must
            // not be usable against an arbitrary bucket just because a request names one.
            S3StorageProfileResolver resolver = CreateResolver();

            resolver.IsBucketAllowed("minio-dev", "some-other-tenants-bucket").Should().BeFalse();
        }

        /// <summary>
        /// Audit S-4: ForcePathStyle defaulted to true for every profile, which breaks real AWS S3
        /// buckets that only accept virtual-hosted requests. Unset now means path style for a
        /// custom endpoint (the old behaviour for MinIO and friends) and virtual-hosted for AWS;
        /// an explicit setting always wins. Bound from configuration, the way profiles are loaded.
        /// </summary>
        [Theory]
        [InlineData(null, null, false)]
        [InlineData("http://minio:9000", null, true)]
        [InlineData(null, "true", true)]
        [InlineData("http://minio:9000", "false", false)]
        public void UsePathStyle_DefaultsByEndpointAndHonoursAnExplicitSetting(string? serviceUrl, string? forcePathStyle, bool expected)
        {
            Dictionary<string, string?> values = new() { ["Profiles:0:Name"] = "p" };
            if (serviceUrl is not null)
            {
                values["Profiles:0:ServiceUrl"] = serviceUrl;
            }
            if (forcePathStyle is not null)
            {
                values["Profiles:0:ForcePathStyle"] = forcePathStyle;
            }
            IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

            S3StorageProfileConfig[] profiles = configuration.GetSection("Profiles").Get<S3StorageProfileConfig[]>()!;

            profiles.Should().ContainSingle().Which.UsePathStyle.Should().Be(expected);
        }

        [Fact]
        public void IsBucketAllowed_UnknownProfile_ReturnsFalse()
        {
            S3StorageProfileResolver resolver = CreateResolver();

            resolver.IsBucketAllowed("does-not-exist", "dataset-bucket").Should().BeFalse();
        }
    }
}
