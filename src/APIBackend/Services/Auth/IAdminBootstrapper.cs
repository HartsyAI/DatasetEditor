namespace DatasetStudio.APIBackend.Services.Auth;

/// <summary>Ensures a working admin account exists, driven by ADMIN_USERNAME/ADMIN_EMAIL/ADMIN_PASSWORD.</summary>
public interface IAdminBootstrapper
{
    Task RunAsync(CancellationToken cancellationToken = default);
}
