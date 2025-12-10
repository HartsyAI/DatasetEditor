namespace DatasetStudio.Core.Enumerations;

/// <summary>Defines available theme modes for the application UI</summary>
public enum ThemeMode
{
    /// <summary>Light theme</summary>
    Light = 0,

    /// <summary>Dark theme (default)</summary>
    Dark = 1,

    /// <summary>Auto theme based on system preference - TODO: Implement system detection</summary>
    Auto = 2,

    /// <summary>High contrast theme for accessibility - TODO: Implement high contrast</summary>
    HighContrast = 3
}
