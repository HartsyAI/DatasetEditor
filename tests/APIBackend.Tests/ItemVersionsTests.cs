using System;
using DatasetStudio.APIBackend.Services.DatasetManagement;
using FluentAssertions;
using Xunit;

namespace DatasetStudio.Tests.APIBackend
{
    /// <summary>Audit E-4: the If-Match rules and the version an edit stamps.</summary>
    public sealed class ItemVersionsTests
    {
        [Theory]
        [InlineData(null, true)]
        [InlineData("", true)]
        [InlineData("*", true)]
        [InlineData("\"42\"", true)]
        [InlineData("W/\"42\"", true)]
        [InlineData("\"7\", \"42\"", true)]
        [InlineData("42", true)]
        [InlineData("\"41\"", false)]
        [InlineData("\"7\", \"8\"", false)]
        public void IfMatchAllows_FollowsTheHeaderRules(string? ifMatch, bool allowed)
        {
            ItemVersions.IfMatchAllows(ifMatch, "42").Should().Be(allowed);
        }

        [Fact]
        public void NextUpdatedAt_MovesForwardEvenWithinOneMillisecond()
        {
            DateTime previous = new(2026, 11, 9, 12, 0, 0, 500, DateTimeKind.Utc);

            DateTime next = ItemVersions.NextUpdatedAt(previous, utcNow: previous);

            next.Should().BeAfter(previous);
            ItemVersions.ForLocal(next).Should().NotBe(ItemVersions.ForLocal(previous));
        }

        [Fact]
        public void NextUpdatedAt_IsNowToTheMillisecond()
        {
            DateTime now = new DateTime(2026, 11, 9, 12, 0, 0, 500, DateTimeKind.Utc).AddTicks(1234);

            DateTime next = ItemVersions.NextUpdatedAt(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), now);

            next.Should().Be(new DateTime(2026, 11, 9, 12, 0, 0, 500, DateTimeKind.Utc));
        }
    }
}
