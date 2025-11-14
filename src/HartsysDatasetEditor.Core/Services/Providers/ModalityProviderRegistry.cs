using HartsysDatasetEditor.Core.Enums;
using HartsysDatasetEditor.Core.Interfaces;
using HartsysDatasetEditor.Core.Utilities;

namespace HartsysDatasetEditor.Core.Services.Providers;

/// <summary>Registry for managing modality providers. Implements provider/plugin pattern for extensibility.</summary>
public class ModalityProviderRegistry
{
    private readonly Dictionary<Modality, IModalityProvider> _providers = new();
    
    /// <summary>Initializes the registry and registers default providers</summary>
    public ModalityProviderRegistry()
    {
        RegisterDefaultProviders();
    }
    
    /// <summary>Registers default built-in modality providers</summary>
    private void RegisterDefaultProviders()
    {
        // Register image modality provider
        Register(new ImageModalityProvider());
        
        Logs.Info($"Registered {_providers.Count} default modality providers");
        
        // TODO: Register text modality provider when implemented
        // TODO: Register video modality provider when implemented
        // TODO: Register 3D modality provider when implemented
        // TODO: Auto-discover and register providers using reflection
    }
    
    /// <summary>Registers a modality provider</summary>
    public void Register(IModalityProvider provider)
    {
        if (provider == null)
        {
            throw new ArgumentNullException(nameof(provider));
        }
        
        if (_providers.ContainsKey(provider.ModalityType))
        {
            Logs.Warning($"Modality provider for {provider.ModalityType} is already registered. Replacing.");
        }
        
        _providers[provider.ModalityType] = provider;
        Logs.Info($"Registered modality provider: {provider.Name} (Modality: {provider.ModalityType})");
    }
    
    /// <summary>Unregisters a modality provider</summary>
    public void Unregister(Modality modality)
    {
        if (_providers.Remove(modality))
        {
            Logs.Info($"Unregistered modality provider for: {modality}");
        }
    }
    
    /// <summary>Gets a provider for a specific modality</summary>
    public IModalityProvider? GetProvider(Modality modality)
    {
        if (_providers.TryGetValue(modality, out IModalityProvider? provider))
        {
            return provider;
        }
        
        Logs.Warning($"No provider registered for modality: {modality}");
        return null;
    }
    
    /// <summary>Gets all registered providers</summary>
    public IReadOnlyDictionary<Modality, IModalityProvider> GetAllProviders()
    {
        return _providers;
    }
    
    /// <summary>Checks if a provider exists for a modality</summary>
    public bool HasProvider(Modality modality)
    {
        return _providers.ContainsKey(modality);
    }
    
    /// <summary>Gets supported modalities (those with registered providers)</summary>
    public List<Modality> GetSupportedModalities()
    {
        return _providers.Keys.ToList();
    }
    
    /// <summary>Clears all registered providers</summary>
    public void Clear()
    {
        int count = _providers.Count;
        _providers.Clear();
        Logs.Info($"Cleared {count} modality providers from registry");
    }
    
    // TODO: Add support for provider health checks
    // TODO: Add support for provider capabilities querying
    // TODO: Add support for provider priority/fallback chains
}
