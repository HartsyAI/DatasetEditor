namespace DatasetStudio.Core.Abstractions;

/// <summary>Defines a layout option for displaying dataset items</summary>
public interface ILayoutProvider
{
    /// <summary>Unique layout identifier</summary>
    string LayoutId { get; }

    /// <summary>Display name for UI</summary>
    string LayoutName { get; }

    /// <summary>Description of the layout</summary>
    string Description { get; }

    /// <summary>Icon name (MudBlazor icon)</summary>
    string IconName { get; }

    /// <summary>Default number of columns (if applicable)</summary>
    int DefaultColumns { get; }

    /// <summary>Minimum columns allowed</summary>
    int MinColumns { get; }

    /// <summary>Maximum columns allowed</summary>
    int MaxColumns { get; }

    /// <summary>Whether column adjustment is supported</summary>
    bool SupportsColumnAdjustment { get; }

    /// <summary>Razor component type name to render</summary>
    string ComponentName { get; }
}
