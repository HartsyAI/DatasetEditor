using DatasetStudio.ClientApp.Shared.Presentation;
using DatasetStudio.DTO.Datasets;
using Microsoft.AspNetCore.Components;

namespace DatasetStudio.ClientApp.Shared.Components;

/// <summary>The one rendering of where a dataset's items come from (DESIGN.md §5.4).</summary>
public partial class SourceBadge
{
    /// <summary>The dataset's stored source type.</summary>
    [Parameter, EditorRequired] public DatasetSourceType SourceType { get; set; }

    /// <summary>Whether items are streamed from the source rather than stored locally.</summary>
    [Parameter] public bool IsStreaming { get; set; }

    /// <summary>Extra CSS classes for spacing in the parent layout.</summary>
    [Parameter] public string? Class { get; set; }

    private SourcePresentation _presentation = SourcePresentation.For(DatasetSourceType.LocalUpload, false);

    /// <summary>Recomputes the presentation when the parameters change.</summary>
    protected override void OnParametersSet()
    {
        _presentation = SourcePresentation.For(SourceType, IsStreaming);
    }
}
