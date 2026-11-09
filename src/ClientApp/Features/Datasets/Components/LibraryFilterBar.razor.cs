using DatasetStudio.DTO.Datasets;
using Microsoft.AspNetCore.Components;

namespace DatasetStudio.ClientApp.Features.Datasets.Components;

/// <summary>
/// Search, status, source and "only ready" controls for the datasets page (DESIGN.md §4.1). Labels
/// come from the shared status and source presentation, so the filter reads "Ready" and
/// "Uploaded file" exactly like the chips on the cards it filters.
/// </summary>
public partial class LibraryFilterBar
{
    private static readonly IngestionStatusDto[] _statuses =
    [
        IngestionStatusDto.Pending,
        IngestionStatusDto.Processing,
        IngestionStatusDto.Completed,
        IngestionStatusDto.Failed
    ];

    // HuggingFace and HuggingFaceDownload share a value, so one entry covers imported repositories.
    private static readonly DatasetSourceType[] _sources =
    [
        DatasetSourceType.LocalUpload,
        DatasetSourceType.LocalFolder,
        DatasetSourceType.HuggingFace,
        DatasetSourceType.HuggingFaceStreaming,
        DatasetSourceType.ExternalS3Streaming
    ];

    /// <summary>The current filter.</summary>
    [Parameter, EditorRequired] public LibraryFilter Filter { get; set; } = LibraryFilter.None;

    /// <summary>Raised with the whole new filter whenever any control changes.</summary>
    [Parameter] public EventCallback<LibraryFilter> FilterChanged { get; set; }

    private Task OnSearchChanged(string? value) => FilterChanged.InvokeAsync(Filter with { Search = value ?? string.Empty });

    private Task OnStatusChanged(IngestionStatusDto? value) => FilterChanged.InvokeAsync(Filter with { Status = value });

    private Task OnSourceChanged(DatasetSourceType? value) => FilterChanged.InvokeAsync(Filter with { Source = value });

    private Task OnOnlyReadyChanged(bool value) => FilterChanged.InvokeAsync(Filter with { OnlyReady = value });
}
