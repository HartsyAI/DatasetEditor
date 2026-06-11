using DatasetStudio.DTO.Datasets;
using Xunit;

namespace DatasetStudio.Tests.ClientApp
{
    public sealed class DatasetSourceTypeTests
    {
        [Fact]
        public void HuggingFaceDownload_IsAliasOfHuggingFace()
        {
            DatasetSourceType baseType = DatasetSourceType.HuggingFace;
            DatasetSourceType aliasType = DatasetSourceType.HuggingFaceDownload;

            Assert.Equal(baseType, aliasType);
        }

        [Fact]
        public void ExternalS3Streaming_HasDistinctValue()
        {
            DatasetSourceType external = DatasetSourceType.ExternalS3Streaming;

            Assert.NotEqual(DatasetSourceType.LocalUpload, external);
            Assert.NotEqual(DatasetSourceType.HuggingFace, external);
            Assert.NotEqual(DatasetSourceType.HuggingFaceStreaming, external);
            Assert.NotEqual(DatasetSourceType.WebUrl, external);
            Assert.NotEqual(DatasetSourceType.LocalFolder, external);
        }
    }
}
