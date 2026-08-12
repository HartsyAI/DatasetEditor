using DatasetStudio.APIBackend.Services.Storage;
using FluentAssertions;
using Xunit;

namespace DatasetStudio.Tests.APIBackend
{
    /// <summary>
    /// S3KeySafety is the S3 equivalent of ServeDatasetFile's local-path containment check --
    /// same sibling-prefix false-positive class Phase 1 hardened the local check against
    /// ("photos" must not match "photos-other-tenant/x.jpg"), plus a path-traversal guard.
    /// </summary>
    public sealed class S3KeySafetyTests
    {
        [Theory]
        [InlineData("images/", "images/photo.jpg")]
        [InlineData("images", "images/photo.jpg")] // non-slash-terminated prefix normalizes the same way
        [InlineData("images/", "images/nested/photo.jpg")]
        public void IsKeyWithinPrefix_KeyGenuinelyUnderPrefix_ReturnsTrue(string prefix, string key)
        {
            S3KeySafety.IsKeyWithinPrefix(prefix, key).Should().BeTrue();
        }

        [Fact]
        public void IsKeyWithinPrefix_SiblingPrefixFalsePositive_ReturnsFalse()
        {
            // The exact false-positive class local ServeDatasetFile guards against: a bare
            // (non-slash-terminated) prefix must not match a key that merely starts with the
            // same characters but belongs to a different, sibling prefix.
            S3KeySafety.IsKeyWithinPrefix("images", "images-other-tenant/secret.jpg").Should().BeFalse();
        }

        [Fact]
        public void IsKeyWithinPrefix_KeyOutsidePrefix_ReturnsFalse()
        {
            S3KeySafety.IsKeyWithinPrefix("images/", "other/photo.jpg").Should().BeFalse();
        }

        [Fact]
        public void IsKeyWithinPrefix_NullOrEmptyPrefix_AllowsAnyWellFormedKey()
        {
            S3KeySafety.IsKeyWithinPrefix(null, "anything/at-all.jpg").Should().BeTrue();
            S3KeySafety.IsKeyWithinPrefix(string.Empty, "anything/at-all.jpg").Should().BeTrue();
        }

        [Theory]
        [InlineData("images/../../../etc/passwd")]
        [InlineData("../secret.jpg")]
        public void IsKeyWithinPrefix_PathTraversalInKey_ReturnsFalseRegardlessOfPrefix(string key)
        {
            S3KeySafety.IsKeyWithinPrefix(null, key).Should().BeFalse();
            S3KeySafety.IsKeyWithinPrefix("images/", key).Should().BeFalse();
        }

        [Fact]
        public void IsKeyWithinPrefix_EmptyKey_ReturnsFalse()
        {
            S3KeySafety.IsKeyWithinPrefix("images/", string.Empty).Should().BeFalse();
        }

        [Fact]
        public void IsKeyWithinPrefix_KeyExactlyEqualsPrefix_ReturnsTrue()
        {
            S3KeySafety.IsKeyWithinPrefix("images/cover.jpg", "images/cover.jpg").Should().BeTrue();
        }
    }
}
