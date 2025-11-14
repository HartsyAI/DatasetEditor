using HartsysDatasetEditor.Api.Services;

namespace HartsysDatasetEditor.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDatasetServices(this IServiceCollection services)
    {
        services.AddSingleton<IDatasetRepository, InMemoryDatasetRepository>();
        services.AddSingleton<IDatasetItemRepository, InMemoryDatasetItemRepository>();
        services.AddSingleton<IDatasetIngestionService, NoOpDatasetIngestionService>();

        return services;
    }
}
