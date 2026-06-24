using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using DatasetStudio.DTO.Datasets;

namespace DatasetStudio.ClientApp.Features.Datasets.Components;

/// <summary>
/// Row-virtualized renderer over a flat item window. Wraps Blazor's <c>&lt;Virtualize&gt;</c>:
/// each virtualized element is one row of <see cref="Columns"/> items, so only a few
/// screenfuls of cards exist in the DOM regardless of dataset size. Works for both the
/// image grid (square cards, computed row height) and the list (fixed row height).
/// </summary>
public partial class VirtualizedItemView : IAsyncDisposable
{
    /// <summary>The in-memory item window to render (already paged/buffered by the cache).</summary>
    [Parameter] public IReadOnlyList<DatasetItemDto> Items { get; set; } = Array.Empty<DatasetItemDto>();

    /// <summary>Number of columns per row (1 for list view).</summary>
    [Parameter] public int Columns { get; set; } = 4;

    /// <summary>Template rendered for each item.</summary>
    [Parameter] public RenderFragment<DatasetItemDto> ItemTemplate { get; set; } = default!;

    /// <summary>Optional content shown when there are no items.</summary>
    [Parameter] public RenderFragment? EmptyContent { get; set; }

    /// <summary>Invoked when the user scrolls within <see cref="PrefetchRows"/> of the end.</summary>
    [Parameter] public EventCallback OnNearEnd { get; set; }

    /// <summary>When set (e.g. list view), use this fixed row height instead of computing square cards.</summary>
    [Parameter] public double? FixedRowHeight { get; set; }

    /// <summary>How many rows from the end trigger a prefetch.</summary>
    [Parameter] public int PrefetchRows { get; set; } = 3;

    /// <summary>Rows rendered above/below the viewport by Virtualize.</summary>
    [Parameter] public int OverscanRows { get; set; } = 2;

    private const double Gap = 16;
    private const double SidePadding = 16;

    private ElementReference _container;
    private DotNetObjectReference<VirtualizedItemView>? _selfRef;
    private List<int> _rowIndices = new();
    private float _rowHeight = 260f;
    private double _containerWidth;
    private int _lastItemCount = -1;
    private int _lastColumns = -1;
    private object? _lastItemsRef;
    private int _prefetchedAtCount = -1;

    private int ColumnCount => Columns < 1 ? 1 : Columns;
    private int RowCount => _rowIndices.Count;

    protected override void OnParametersSet()
    {
        bool changed = !ReferenceEquals(_lastItemsRef, Items)
            || Items.Count != _lastItemCount
            || ColumnCount != _lastColumns;

        if (changed)
        {
            RebuildRows();
            RecomputeRowHeight();
            _lastItemsRef = Items;
            _lastItemCount = Items.Count;
            _lastColumns = ColumnCount;
        }
    }

    private void RebuildRows()
    {
        int cols = ColumnCount;
        int rows = (Items.Count + cols - 1) / cols;
        if (_rowIndices.Count != rows)
        {
            var indices = new List<int>(rows);
            for (int i = 0; i < rows; i++)
            {
                indices.Add(i);
            }
            _rowIndices = indices;
        }
    }

    private IEnumerable<DatasetItemDto> RowItems(int rowIndex)
    {
        int cols = ColumnCount;
        int start = rowIndex * cols;
        int end = Math.Min(start + cols, Items.Count);
        for (int i = start; i < end; i++)
        {
            yield return Items[i];
        }
    }

    /// <summary>
    /// Fires the prefetch callback once per item-count when a near-end row renders.
    /// Virtualize only renders visible rows, so this naturally triggers as the user
    /// approaches the bottom. Guarded so it fires at most once per buffered batch.
    /// </summary>
    private void MaybePrefetch(int rowIndex)
    {
        if (!OnNearEnd.HasDelegate)
        {
            return;
        }
        if (rowIndex < RowCount - PrefetchRows)
        {
            return;
        }
        if (_prefetchedAtCount == Items.Count)
        {
            return;
        }
        _prefetchedAtCount = Items.Count;
        _ = OnNearEnd.InvokeAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        _selfRef = DotNetObjectReference.Create(this);
        try
        {
            _containerWidth = await JS.InvokeAsync<double>("viewportInterop.getWidth", _container);
            RecomputeRowHeight();
            await JS.InvokeVoidAsync("viewportInterop.observeWidth", _container, _selfRef);
            StateHasChanged();
        }
        catch
        {
            // JS interop unavailable (e.g. prerender); keep the default row height.
        }
    }

    /// <summary>Called from JS when the container width changes (responsive resize).</summary>
    [JSInvokable]
    public void OnContainerResized(double width)
    {
        if (Math.Abs(width - _containerWidth) < 1)
        {
            return;
        }
        _containerWidth = width;
        RecomputeRowHeight();
        StateHasChanged();
    }

    private void RecomputeRowHeight()
    {
        if (FixedRowHeight.HasValue)
        {
            _rowHeight = (float)FixedRowHeight.Value;
            return;
        }
        if (_containerWidth <= 0)
        {
            return;
        }
        int cols = ColumnCount;
        double cardWidth = (_containerWidth - (2 * SidePadding) - ((cols - 1) * Gap)) / cols;
        if (cardWidth < 40)
        {
            cardWidth = 40;
        }
        _rowHeight = (float)(cardWidth + Gap); // square cards (aspect-ratio 1/1) plus the row gap
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("viewportInterop.unobserve", _container);
        }
        catch
        {
            // Ignore disposal-time interop failures.
        }
        _selfRef?.Dispose();
    }
}
