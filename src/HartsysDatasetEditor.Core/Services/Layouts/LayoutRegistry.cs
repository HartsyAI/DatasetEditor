using HartsysDatasetEditor.Core.Interfaces;
using HartsysDatasetEditor.Core.Utilities;

namespace HartsysDatasetEditor.Core.Services.Layouts;

/// <summary>Registry for all available layout providers</summary>
public class LayoutRegistry
{
    private readonly Dictionary<string, ILayoutProvider> _layouts = new();
    
    public LayoutRegistry()
    {
        RegisterDefaultLayouts();
    }
    
    /// <summary>Registers default layouts</summary>
    private void RegisterDefaultLayouts()
    {
        Register(new StandardGridLayout());
        Register(new ListLayout());
        Register(new MasonryLayout());
        Register(new SlideshowLayout());
        
        Logs.Info($"Registered {_layouts.Count} layout providers");
    }
    
    /// <summary>Registers a layout provider</summary>
    public void Register(ILayoutProvider layout)
    {
        _layouts[layout.LayoutId] = layout;
        Logs.Info($"Registered layout: {layout.LayoutName}");
    }
    
    /// <summary>Gets a layout by ID</summary>
    public ILayoutProvider? GetLayout(string layoutId)
    {
        return _layouts.GetValueOrDefault(layoutId);
    }
    
    /// <summary>Gets all registered layouts</summary>
    public List<ILayoutProvider> GetAllLayouts()
    {
        return _layouts.Values.ToList();
    }
    
    /// <summary>Gets the default layout</summary>
    public ILayoutProvider GetDefaultLayout()
    {
        return _layouts["grid"];
    }
}
