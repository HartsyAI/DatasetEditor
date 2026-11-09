using DatasetStudio.ClientApp.Shared.Presentation;
using DatasetStudio.DTO.Datasets;
using MudBlazor;
using Xunit;

namespace DatasetStudio.Tests.ClientApp;

/// <summary>
/// PR 2.2 (audit U-17): status and source presentation is one mapping shared by every page.
/// The library and viewer each had their own switch and disagreed (Pending was Warning, the
/// API word "Completed" was shown instead of "Ready", server folders had no source label).
/// </summary>
public sealed class PresentationTests
{
    /// <summary>Each status gets exactly the DESIGN.md §5.3 colour, alert severity, icon and label.</summary>
    [Theory]
    [InlineData(IngestionStatusDto.Pending, Color.Default, Severity.Normal, "Pending")]
    [InlineData(IngestionStatusDto.Processing, Color.Info, Severity.Info, "Processing")]
    [InlineData(IngestionStatusDto.Completed, Color.Success, Severity.Success, "Ready")]
    [InlineData(IngestionStatusDto.Failed, Color.Error, Severity.Error, "Failed")]
    public void Status_MapsToTheDesignSystemPresentation(IngestionStatusDto status, Color color, Severity severity, string label)
    {
        StatusPresentation presentation = StatusPresentation.For(status);

        Assert.Equal(color, presentation.Color);
        Assert.Equal(severity, presentation.Severity);
        Assert.Equal(label, presentation.Label);
    }

    /// <summary>Status icons follow the fixed icon vocabulary in DESIGN.md §2.6.</summary>
    [Fact]
    public void Status_UsesTheFixedIconVocabulary()
    {
        Assert.Equal(Icons.Material.Filled.Schedule, StatusPresentation.For(IngestionStatusDto.Pending).Icon);
        Assert.Equal(Icons.Material.Filled.Sync, StatusPresentation.For(IngestionStatusDto.Processing).Icon);
        Assert.Equal(Icons.Material.Filled.CheckCircle, StatusPresentation.For(IngestionStatusDto.Completed).Icon);
        Assert.Equal(Icons.Material.Filled.Error, StatusPresentation.For(IngestionStatusDto.Failed).Icon);
    }

    /// <summary>A status value the client doesn't know yet still renders, as a neutral chip.</summary>
    [Fact]
    public void Status_UnknownValueIsNeutral()
    {
        StatusPresentation presentation = StatusPresentation.For((IngestionStatusDto)99);

        Assert.Equal(Color.Default, presentation.Color);
        Assert.Equal("Unknown", presentation.Label);
    }

    /// <summary>Every source type has the DESIGN.md §5.4 label (server folders and URLs had none).</summary>
    [Theory]
    [InlineData(DatasetSourceType.LocalUpload, false, "Uploaded file")]
    [InlineData(DatasetSourceType.LocalFolder, false, "Server folder")]
    [InlineData(DatasetSourceType.HuggingFaceStreaming, true, "HuggingFace (streaming)")]
    [InlineData(DatasetSourceType.HuggingFaceDownload, false, "HuggingFace (imported)")]
    [InlineData(DatasetSourceType.ExternalS3Streaming, true, "S3 bucket")]
    [InlineData(DatasetSourceType.WebUrl, false, "URL")]
    public void Source_MapsToTheDesignSystemLabel(DatasetSourceType sourceType, bool isStreaming, string label)
    {
        Assert.Equal(label, SourcePresentation.For(sourceType, isStreaming).Label);
    }

    /// <summary>
    /// Older HuggingFace imports store the download alias but stream their items; they must read
    /// as streaming, as the library's old "(streaming)" suffix did.
    /// </summary>
    [Fact]
    public void Source_StreamingHuggingFaceDownloadAliasReadsAsStreaming()
    {
        Assert.Equal("HuggingFace (streaming)", SourcePresentation.For(DatasetSourceType.HuggingFaceDownload, isStreaming: true).Label);
    }
}
