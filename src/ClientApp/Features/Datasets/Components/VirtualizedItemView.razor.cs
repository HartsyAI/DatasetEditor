using DatasetStudio.DTO.Datasets;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace DatasetStudio.ClientApp.Features.Datasets.Components;

/// <summary>
/// Row-virtualized renderer over a flat item window. Wraps Blazor's <c>&lt;Virtualize&gt;</c>:
/// each virtualized element is one row of items, so only a few screenfuls of cards exist in the
/// DOM regardless of dataset size. The column count comes from the container width
/// (<see cref="GridLayout.ColumnsFor"/>), resize notifications are debounced in JS, and
/// <see cref="OnNearEnd"/> fires from an IntersectionObserver on an end-of-list sentinel. Before,
/// every ResizeObserver tick re-rendered and the prefetch check ran inside the row template (U-29).
/// </summary>
public partial class VirtualizedItemView : IAsyncDisposable
{
    [Inject] private IJSRuntime JS { get; set; } = default!;

    /// <summary>The in-memory item window to render (already paged/buffered by the cache).</summary>
    [Parameter] public IReadOnlyList<DatasetItemDto> Items { get; set; } = Array.Empty<DatasetItemDto>();

    /// <summary>Preferred columns per row; fewer are used when the container is too narrow (1 for list view).</summary>
    [Parameter] public int Columns { get; set; } = 4;

    /// <summary>Template rendered for each item.</summary>
    [Parameter] public RenderFragment<DatasetItemDto> ItemTemplate { get; set; } = default!;

    /// <summary>Optional content shown when there are no items.</summary>
    [Parameter] public RenderFragment? EmptyContent { get; set; }

    /// <summary>Invoked when the end of the list nears the viewport; at most once per item count.</summary>
    [Parameter] public EventCallback OnNearEnd { get; set; }

    /// <summary>When set (list view), use this fixed row height instead of computing square cards.</summary>
    [Parameter] public double? FixedRowHeight { get; set; }

    /// <summary>
    /// Identifies the current result set (DatasetState.ResultSetVersion). When it changes the
    /// items were replaced, not appended, so load-more tracking starts over.
    /// </summary>
    [Parameter] public int ResultSet { get; set; }

    /// <summary>Rows rendered above/below the viewport by Virtualize.</summary>
    [Parameter] public int OverscanRows { get; set; } = 2;

    /// <summary>How far below the viewport (px) the sentinel triggers <see cref="OnNearEnd"/>.</summary>
    [Parameter] public int PrefetchMarginPx { get; set; } = 800;

    private ElementReference _container;
    private ElementReference _sentinel;
    private DotNetObjectReference<VirtualizedItemView>? _selfRef;
    private List<int> _rowIndices = new();
    private float _rowHeight = 260f;
    private double _containerWidth;
    private int _columns = 1;
    private int _nearEndFiredAtCount = -1;
    // Item count at which the sentinel was last (re)observed; -1 before the first observe.
    private int _observedAtCount = -1;
    private int _resultSet = int.MinValue;

    /// <summary>
    /// Recomputes columns, rows and row height for the current items and width, and resets
    /// load-more tracking for a new result set. A filter that replaced two loaded pages with 100
    /// matches left the "already fired at 100" mark from the old list, so the next page never loaded.
    /// </summary>
    protected override void OnParametersSet()
    {
        if (ResultSet != _resultSet)
        {
            _resultSet = ResultSet;
            _nearEndFiredAtCount = -1;
            _observedAtCount = -1;
        }
        Relayout();
    }

    private void Relayout()
    {
        _columns = FixedRowHeight.HasValue ? 1 : GridLayout.ColumnsFor(_containerWidth, Columns);

        int rows = (Items.Count + _columns - 1) / _columns;
        if (_rowIndices.Count != rows)
        {
            _rowIndices = Enumerable.Range(0, rows).ToList();
        }

        if (FixedRowHeight.HasValue)
        {
            _rowHeight = (float)FixedRowHeight.Value;
        }
        else if (_containerWidth > 0)
        {
            _rowHeight = (float)GridLayout.SquareRowHeight(_containerWidth, _columns);
        }
    }

    private IEnumerable<DatasetItemDto> RowItems(int rowIndex)
    {
        int start = rowIndex * _columns;
        int end = Math.Min(start + _columns, Items.Count);
        for (int i = start; i < end; i++)
        {
            yield return Items[i];
        }
    }

    /// <summary>Measures the container, starts the resize observer, and (once items exist) the sentinel.</summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        try
        {
            if (firstRender)
            {
                _selfRef = DotNetObjectReference.Create(this);
                _containerWidth = await JS.InvokeAsync<double>("viewportInterop.getWidth", _container);
                await JS.InvokeVoidAsync("viewportInterop.observeWidth", _container, _selfRef);
                Relayout();
                StateHasChanged();
            }

            // Re-observe whenever the item count changes. IntersectionObserver reports only changes,
            // so a page that still leaves the end in view would otherwise never trigger the next load;
            // a fresh observe delivers the current state at once. Loading therefore continues only
            // while the end of the list is near the viewport, never through the whole dataset (U-5).
            if (Items.Count > 0 && Items.Count != _observedAtCount && _selfRef is not null)
            {
                _observedAtCount = Items.Count;
                await JS.InvokeVoidAsync("viewportInterop.observeSentinel", _sentinel, _container, _selfRef, PrefetchMarginPx);
            }
        }
        catch (JSException)
        {
            // Interop unavailable; keep the default geometry. The grid still renders.
        }
        catch (JSDisconnectedException)
        {
            // Page is unloading.
        }
    }

    /// <summary>Called from JS (debounced) when the container width changes.</summary>
    [JSInvokable]
    public void OnContainerResized(double width)
    {
        if (Math.Abs(width - _containerWidth) < 1)
        {
            return;
        }
        _containerWidth = width;
        Relayout();
        StateHasChanged();
    }

    /// <summary>
    /// Called from JS when the end-of-list sentinel comes within <see cref="PrefetchMarginPx"/> of
    /// the viewport. Fires <see cref="OnNearEnd"/> at most once per item count, so one load-more
    /// per appended page rather than a request chain.
    /// </summary>
    [JSInvokable]
    public Task OnSentinelVisible()
    {
        if (!OnNearEnd.HasDelegate || _nearEndFiredAtCount == Items.Count)
        {
            return Task.CompletedTask;
        }
        _nearEndFiredAtCount = Items.Count;
        return OnNearEnd.InvokeAsync();
    }

    /// <summary>Stops the observers and releases the .NET reference.</summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("viewportInterop.unobserve", _container);
        }
        catch (JSException)
        {
        }
        catch (JSDisconnectedException)
        {
        }
        _selfRef?.Dispose();
    }
}
