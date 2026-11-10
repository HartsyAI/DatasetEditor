using DatasetStudio.APIBackend.DataAccess.PostgreSQL.Repositories;
using DatasetStudio.APIBackend.Services.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace DatasetStudio.APIBackend.Extensions;

/// <summary>
/// Auth-specific DI registrations, kept separate from AddDatasetServices (which predates auth
/// and is named for dataset ingestion/storage, not accounts/permissions).
/// </summary>
public static class AuthServiceCollectionExtensions
{
    public static IServiceCollection AddAuthServices(this IServiceCollection services)
    {
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IPermissionRepository, PermissionRepository>();
        services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IDatasetPermissionService, DatasetPermissionService>();
        services.AddScoped<IAdminBootstrapper, AdminBootstrapper>();

        return services;
    }
}
