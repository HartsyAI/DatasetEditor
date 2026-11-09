using DatasetStudio.DTO.Datasets;
using Microsoft.AspNetCore.Components;

namespace DatasetStudio.ClientApp.Features.Datasets.Components;

/// <summary>
/// The viewer's detail panel (DESIGN.md §3.1). At <c>md</c> and wider it is a column beside the
/// grid; below <c>md</c> it is a bottom sheet over the grid. It used to be a <c>Md="3"</c> grid
/// column with no small-screen size inside a <c>100vh; overflow:hidden</c> grid, so below 960 px it
/// was stacked under the items and clipped out of reach (audit U-20).
/// </summary>
public partial class DetailSidebar
{
    /// <summary>The selected item, or null when nothing is selected.</summary>
    [Parameter] public DatasetItemDto? Item { get; set; }

    /// <summary>Whether the user has the panel turned on.</summary>
    [Parameter] public bool Open { get; set; }

    /// <summary>Closes the panel (the sheet's close button, or tapping outside it).</summary>
    [Parameter] public EventCallback OnClose { get; set; }

    private Task OnSheetOpenChanged(bool open)
    {
        return open ? Task.CompletedTask : OnClose.InvokeAsync();
    }
}
