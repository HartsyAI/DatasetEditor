using Microsoft.Extensions.DependencyInjection;
using DatasetStudio.ClientApp.Services.Interop;

namespace DatasetStudio.ClientApp.Extensions;

/// <summary>
/// Central place to register client-side services for dependency injection.
/// TODO: Invoke from Program.cs once wiring order is confirmed.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds application-specific client services to the DI container.
    /// TODO: Expand as additional services are introduced (state, analytics, etc.).
    /// </summary>
    public static IServiceCollection AddClientServices(this IServiceCollection services)
    {
        // TODO: Evaluate singleton vs scoped lifetimes per service behavior.
        services.AddScoped<FileReaderInterop>();
        services.AddScoped<LocalStorageInterop>();
        services.AddScoped<ImageLazyLoadInterop>();

        return services;
    }
}
