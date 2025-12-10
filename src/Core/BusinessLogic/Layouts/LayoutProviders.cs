using DatasetStudio.Core.Abstractions;

namespace DatasetStudio.Core.BusinessLogic.Layouts;

/// <summary>Standard grid layout with uniform card sizes</summary>
public class StandardGridLayout : ILayoutProvider
{
    public string LayoutId => "grid";
    public string LayoutName => "Grid";
    public string Description => "Standard grid with uniform card sizes";
    public string IconName => "mdi-view-grid";
    public int DefaultColumns => 4;
    public int MinColumns => 1;
    public int MaxColumns => 8;
    public bool SupportsColumnAdjustment => true;
    public string ComponentName => "ImageGrid";
}

/// <summary>List layout with horizontal cards</summary>
public class ListLayout : ILayoutProvider
{
    public string LayoutId => "list";
    public string LayoutName => "List";
    public string Description => "Single column list with detailed information";
    public string IconName => "mdi-view-list";
    public int DefaultColumns => 1;
    public int MinColumns => 1;
    public int MaxColumns => 1;
    public bool SupportsColumnAdjustment => false;
    public string ComponentName => "ImageList";
}

/// <summary>Masonry layout with varying card heights</summary>
public class MasonryLayout : ILayoutProvider
{
    public string LayoutId => "masonry";
    public string LayoutName => "Masonry";
    public string Description => "Pinterest-style layout with varying heights";
    public string IconName => "mdi-view-quilt";
    public int DefaultColumns => 4;
    public int MinColumns => 2;
    public int MaxColumns => 6;
    public bool SupportsColumnAdjustment => true;
    public string ComponentName => "ImageMasonry";
}

/// <summary>Slideshow/carousel layout for single images</summary>
public class SlideshowLayout : ILayoutProvider
{
    public string LayoutId => "slideshow";
    public string LayoutName => "Slideshow";
    public string Description => "Full-screen slideshow with navigation";
    public string IconName => "mdi-slideshow";
    public int DefaultColumns => 1;
    public int MinColumns => 1;
    public int MaxColumns => 1;
    public bool SupportsColumnAdjustment => false;
    public string ComponentName => "ImageSlideshow";
}
