using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Bunit;
using DatasetStudio.ClientApp.Features.Datasets.Components;
using DatasetStudio.ClientApp.Features.Datasets.Services;
using DatasetStudio.ClientApp.Services.ApiClients;
using DatasetStudio.ClientApp.Services.StateManagement;
using DatasetStudio.DTO.Datasets;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using MudBlazor.Services;
using Xunit;

namespace DatasetStudio.Tests.ClientApp;

/// <summary>
/// PR 2.4 review follow-ups, rendered with bUnit: the list row must render items whose facts repeat
/// (two "Unknown" chips) and the image card must not open its item while the user types in the
/// inline title editor. Both failed on the first version of the PR.
/// </summary>
public sealed class ViewerComponentTests : IAsyncLifetime
{
    private readonly BunitContext _context = new();

    /// <summary>Registers the services the viewer components inject, with a loose JS runtime.</summary>
    public ViewerComponentTests()
    {
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddMudServices();
        _context.Services.AddSingleton<IOptions<DatasetApiOptions>>(Options.Create(new DatasetApiOptions { BaseAddress = "http://localhost:5000" }));
        _context.Services.AddSingleton<IWebAssemblyHostEnvironment>(new StubHostEnvironment());
        _context.Services.AddSingleton<ImageUrlHelper>();
        _context.Services.AddSingleton(new DatasetApiClient(new HttpClient { BaseAddress = new Uri("http://localhost:5000/") }));
        _context.Services.AddSingleton<DatasetState>();
        _context.Services.AddSingleton<ViewState>();
        _context.Services.AddSingleton<ItemEditService>();
        // Tooltips need a rendered popover provider, as MainLayout supplies in the app.
        _context.Render<MudBlazor.MudPopoverProvider>();
    }

    /// <summary>
    /// An item with no dimensions and no recognisable extension has two "Unknown" facts. Keying the
    /// chips by their text gave duplicate sibling keys, and Blazor threw while rendering the list.
    /// </summary>
    [Fact]
    public void ListRow_RendersItemsWhoseFactsRepeat()
    {
        DatasetItemDto item = new() { Id = Guid.NewGuid(), Title = "no image", Tags = new List<string> { "a", "a" } };

        IRenderedComponent<ImageListRow> row = _context.Render<ImageListRow>(p => p.Add(r => r.Item, item));
        // Duplicate keys are only detected when Blazor diffs a re-render (selecting the row does one).
        row.Render(p => p.Add(r => r.IsSelected, true));

        Assert.True(row.FindAll(".mud-chip").Count(c => c.TextContent.Trim() == "Unknown") >= 2);
        Assert.Contains("no image", row.Markup);
    }

    /// <summary>
    /// Typing a space (or Enter) in the card's title editor must not open the item: the editor's
    /// keydown used to bubble to the card's Enter/Space handler.
    /// </summary>
    [Fact]
    public void Card_TitleEditorKeysDoNotOpenTheItem()
    {
        int opened = 0;
        DatasetItemDto item = new() { Id = Guid.NewGuid(), Title = "first", ImageUrl = "/a.jpg" };
        IRenderedComponent<ImageCard> card = _context.Render<ImageCard>(p => p
            .Add(c => c.Item, item)
            .Add(c => c.OnClick, EventCallback.Factory.Create<DatasetItemDto>(this, _ => opened++)));

        card.Find("button[aria-label='Edit title']").Click();
        card.Find(".bottom-info input").KeyDown(" ");
        card.Find(".bottom-info input").KeyDown("Enter");

        Assert.Equal(0, opened);
    }

    /// <summary>Control: Enter on the card itself still opens the item.</summary>
    [Fact]
    public void Card_EnterOnTheCardOpensTheItem()
    {
        int opened = 0;
        DatasetItemDto item = new() { Id = Guid.NewGuid(), Title = "first", ImageUrl = "/a.jpg" };
        IRenderedComponent<ImageCard> card = _context.Render<ImageCard>(p => p
            .Add(c => c.Item, item)
            .Add(c => c.OnClick, EventCallback.Factory.Create<DatasetItemDto>(this, _ => opened++)));

        card.Find(".modern-image-card").KeyDown("Enter");

        Assert.Equal(1, opened);
    }

    /// <summary>Nothing to set up asynchronously.</summary>
    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>Disposes the bUnit context asynchronously (MudBlazor's popover service requires it).</summary>
    public Task DisposeAsync() => _context.DisposeAsync().AsTask();

    private sealed class StubHostEnvironment : IWebAssemblyHostEnvironment
    {
        public string Environment => "Development";
        public string BaseAddress => "http://localhost:5002/";
    }
}
