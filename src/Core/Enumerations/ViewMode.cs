namespace DatasetStudio.Core.Enumerations;

/// <summary>Defines available view modes for displaying dataset items</summary>
public enum ViewMode
{
    /// <summary>Grid view with cards (default for images)</summary>
    Grid = 0,

    /// <summary>List view with table rows</summary>
    List = 1,

    /// <summary>Full-screen gallery/slideshow view</summary>
    Gallery = 2,

    /// <summary>Masonry layout with varying heights - TODO: Implement masonry layout</summary>
    Masonry = 3,

    /// <summary>Timeline view for sequential data - TODO: Implement for video/audio</summary>
    Timeline = 4
}
