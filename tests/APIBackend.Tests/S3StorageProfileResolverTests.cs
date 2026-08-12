using System.Collections.Generic;
using DatasetStudio.APIBackend.Services.Storage;
using FluentAssertions;
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

        [Fact]
        public void IsBucketAllowed_UnknownProfile_ReturnsFalse()
        {
            S3StorageProfileResolver resolver = CreateResolver();

            resolver.IsBucketAllowed("does-not-exist", "dataset-bucket").Should().BeFalse();
        }
    }
}
