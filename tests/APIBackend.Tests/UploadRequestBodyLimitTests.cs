using DatasetStudio.APIBackend.Configuration;
using FluentAssertions;
using Xunit;

namespace DatasetStudio.Tests.APIBackend
{
    /// <summary>
    /// Pure unit tests for which paths receive the 5 GB upload body limit. They need no Docker or
    /// database, so they run even when the integration suite cannot start. The first theory covers
    /// every GUID format the {datasetId:guid} route constraint accepts: a fixed-length pattern once
    /// rejected the N, B and P forms, so an upload with a valid dataset ID failed with 413 at 10 MB.
    /// </summary>
    public sealed class UploadRequestBodyLimitTests
    {
        [Theory]
        [InlineData("/api/datasets/3f2504e0-4f89-11d3-9a0c-0305e82c3301/upload")]
        [InlineData("/api/datasets/3f2504e04f8911d39a0c0305e82c3301/upload")]
        [InlineData("/api/datasets/{3f2504e0-4f89-11d3-9a0c-0305e82c3301}/upload")]
        [InlineData("/api/datasets/(3f2504e0-4f89-11d3-9a0c-0305e82c3301)/upload")]
        [InlineData("/api/datasets/3f2504e0-4f89-11d3-9a0c-0305e82c3301/upload/")]
        [InlineData("/API/DATASETS/3F2504E0-4F89-11D3-9A0C-0305E82C3301/UPLOAD")]
        public void IsDatasetUploadPath_AcceptsEveryGuidFormatTheRouteAccepts(string path)
        {
            UploadRequestBodyLimit.IsDatasetUploadPath(path).Should().BeTrue();
        }

        [Theory]
        [InlineData("/api/datasets/3f2504e0-4f89-11d3-9a0c-0305e82c3301/items")]
        [InlineData("/api/datasets/3f2504e0-4f89-11d3-9a0c-0305e82c3301")]
        [InlineData("/api/datasets/not-a-guid/upload")]
        [InlineData("/api/datasets//upload")]
        [InlineData("/api/datasets/3f2504e0-4f89-11d3-9a0c-0305e82c3301/upload/extra")]
        [InlineData("/api/items/3f2504e0-4f89-11d3-9a0c-0305e82c3301/upload")]
        [InlineData("/api/datasets/3f2504e0-4f89-11d3-9a0c-0305e82c3301/import-folder")]
        public void IsDatasetUploadPath_RejectsEveryOtherPath(string path)
        {
            UploadRequestBodyLimit.IsDatasetUploadPath(path).Should().BeFalse();
        }

        [Fact]
        public void UploadLimit_IsLargerThanTheDefaultLimitEveryOtherEndpointGets()
        {
            UploadRequestBodyLimit.MaxUploadBodyBytes.Should().BeGreaterThan(UploadRequestBodyLimit.DefaultMaxRequestBodyBytes);
            UploadRequestBodyLimit.DefaultMaxRequestBodyBytes.Should().Be(10L * 1024 * 1024);
        }
    }
}
