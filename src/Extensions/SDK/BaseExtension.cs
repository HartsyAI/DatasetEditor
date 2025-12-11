// TODO: Phase 3 - Extension Infrastructure
//
// Purpose: Define the base class that all extensions must inherit from, providing
// a standardized interface for the extension system to interact with plugins.
//
// Implementation Plan:
// 1. Define base properties and methods required by all extensions
// 2. Implement lifecycle methods (Initialize, Execute, Shutdown)
// 3. Create extension context for dependency injection
// 4. Define event hooks and callbacks
// 5. Implement logging and error handling mechanisms
// 6. Add configuration management methods
// 7. Implement permission/capability checking
//
// Dependencies:
// - ExtensionMetadata.cs
// - IExtensionLogger interface
// - IExtensionContext interface
// - IServiceProvider for DI
// - System.Reflection for plugin discovery
//
// References:
// - See REFACTOR_PLAN.md Phase 3 - Extension System Infrastructure for details
// - Design pattern: Abstract Factory + Template Method
// - Should follow Microsoft Extension Model conventions

namespace DatasetStudio.Extensions.SDK;

/// <summary>
/// Base class for all Dataset Studio extensions.
/// All custom extensions must inherit from this class and implement required methods.
/// </summary>
public abstract class BaseExtension
{
    // TODO: Phase 3 - Add extension lifecycle management
    // Methods needed:
    // - Initialize(IExtensionContext context): Task
    // - OnLoaded(): Task
    // - OnExecute(IExtensionRequest request): Task<IExtensionResponse>
    // - OnShutdown(): Task
    // - Validate(): bool

    /// <summary>
    /// Gets the extension metadata containing name, version, author, etc.
    /// </summary>
    public abstract ExtensionMetadata GetMetadata();

    // TODO: Phase 3 - Add abstract members for extension capabilities
    // Properties needed:
    // - IReadOnlyList<string> Capabilities
    // - IReadOnlyList<string> RequiredPermissions
    // - bool IsEnabled
    // - Version MinimumCoreVersion

    // TODO: Phase 3 - Add extension event handlers
    // Events needed:
    // - event EventHandler<ExtensionInitializedEventArgs> OnInitialized
    // - event EventHandler<ExtensionErrorEventArgs> OnError
    // - event EventHandler<ExtensionExecutedEventArgs> OnExecuted

    // TODO: Phase 3 - Add configuration management
    // Methods needed:
    // - T GetConfiguration<T>() where T : class
    // - void SetConfiguration<T>(T config) where T : class
    // - IDictionary<string, object> GetAllConfiguration()

    // TODO: Phase 3 - Add logging support
    // Methods needed:
    // - void Log(LogLevel level, string message, params object[] args)
    // - void LogError(Exception ex, string message)
    // - void LogDebug(string message)

    // TODO: Phase 3 - Add service resolution
    // Methods needed:
    // - T GetService<T>() where T : class
    // - object GetService(Type serviceType)
    // - bool TryGetService<T>(out T service) where T : class
}
