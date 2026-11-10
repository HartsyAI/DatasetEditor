using System;
using DatasetStudio.APIBackend.Services.DatasetManagement;
using FluentAssertions;
using Xunit;

namespace DatasetStudio.Tests.APIBackend
{
    /// <summary>
    /// Audit E-3: streamed items got a fresh Guid per page request, so they could never be edited.
    /// Their ids are now derived from where the item comes from.
    /// </summary>
    public sealed class StreamingItemIdsTests
    {
        private static readonly Guid DatasetId = Guid.Parse("6f1c2d4e-0000-4000-8000-000000000001");

        [Fact]
        public void HuggingFaceRow_SameInputs_GiveTheSameId()
        {
            Guid first = StreamingItemIds.ForHuggingFaceRow(DatasetId, "owner/name", "default", "train", 42);
            Guid second = StreamingItemIds.ForHuggingFaceRow(DatasetId, "owner/name", "default", "train", 42);

            second.Should().Be(first);
        }

        [Fact]
        public void HuggingFaceRow_AnyPartChanged_GivesADifferentId()
        {
            Guid baseline = StreamingItemIds.ForHuggingFaceRow(DatasetId, "owner/name", "default", "train", 42);

            new[]
            {
                StreamingItemIds.ForHuggingFaceRow(Guid.NewGuid(), "owner/name", "default", "train", 42),
                StreamingItemIds.ForHuggingFaceRow(DatasetId, "owner/other", "default", "train", 42),
                StreamingItemIds.ForHuggingFaceRow(DatasetId, "owner/name", "other", "train", 42),
                StreamingItemIds.ForHuggingFaceRow(DatasetId, "owner/name", null, "train", 42),
                StreamingItemIds.ForHuggingFaceRow(DatasetId, "owner/name", "default", "test", 42),
                StreamingItemIds.ForHuggingFaceRow(DatasetId, "owner/name", "default", "train", 43),
            }.Should().NotContain(baseline).And.OnlyHaveUniqueItems();
        }

        [Fact]
        public void S3Object_SameInputs_GiveTheSameId()
        {
            StreamingItemIds.ForS3Object(DatasetId, "profile", "bucket", "images/a.png")
                .Should().Be(StreamingItemIds.ForS3Object(DatasetId, "profile", "bucket", "images/a.png"));
        }

        [Fact]
        public void S3Object_AnyPartChanged_GivesADifferentId()
        {
            Guid baseline = StreamingItemIds.ForS3Object(DatasetId, "profile", "bucket", "images/a.png");

            new[]
            {
                StreamingItemIds.ForS3Object(Guid.NewGuid(), "profile", "bucket", "images/a.png"),
                StreamingItemIds.ForS3Object(DatasetId, "other", "bucket", "images/a.png"),
                StreamingItemIds.ForS3Object(DatasetId, "profile", "other", "images/a.png"),
                StreamingItemIds.ForS3Object(DatasetId, "profile", "bucket", "images/b.png"),
            }.Should().NotContain(baseline).And.OnlyHaveUniqueItems();
        }

        [Fact]
        public void PartsCannotBeShiftedBetweenFields()
        {
            // Without length prefixes "bucket|a" + "b" and "bucket" + "a|b" would hash alike.
            StreamingItemIds.ForS3Object(DatasetId, "p", "bucket|a", "b")
                .Should().NotBe(StreamingItemIds.ForS3Object(DatasetId, "p", "bucket", "a|b"));
        }

        [Fact]
        public void HuggingFaceAndS3_NeverShareAnId()
        {
            StreamingItemIds.ForHuggingFaceRow(DatasetId, "a", "b", "c", 1)
                .Should().NotBe(StreamingItemIds.ForS3Object(DatasetId, "a", "b", "c"));
        }

        [Fact]
        public void Id_IsAnRfc9562Version8Uuid()
        {
            string text = StreamingItemIds.ForS3Object(DatasetId, "profile", "bucket", "key").ToString();

            text[14].Should().Be('8');
            "89ab".Should().Contain(text[19].ToString());
        }
    }
}
