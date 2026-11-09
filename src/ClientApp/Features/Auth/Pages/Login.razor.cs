using DatasetStudio.ClientApp.Features.Auth.Services;
using DatasetStudio.Core.Utilities.Logging;
using Microsoft.AspNetCore.Components;

namespace DatasetStudio.ClientApp.Features.Auth.Pages;

/// <summary>
/// Log-in page. The form submits on Enter from any field; network failures reset the form with a
/// message (U-28); the return URL is restricted to same-site paths (A-8).
/// </summary>
public partial class Login : ComponentBase
{
    private const string DefaultLandingPage = "/datasets";

    [Inject] private AuthApiClient AuthApiClient { get; set; } = default!;
    [Inject] private CookieAuthenticationStateProvider AuthStateProvider { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    [Parameter]
    [SupplyParameterFromQuery]
    public string? ReturnUrl { get; set; }

    private string _usernameOrEmail = string.Empty;
    private string _password = string.Empty;
    private string? _errorMessage;
    private bool _isSubmitting;

    private bool CanSubmit => !_isSubmitting && !string.IsNullOrWhiteSpace(_usernameOrEmail) && !string.IsNullOrWhiteSpace(_password);

    private async Task SubmitAsync()
    {
        if (!CanSubmit)
        {
            return;
        }

        _isSubmitting = true;
        _errorMessage = null;

        AuthOutcome outcome;
        try
        {
            outcome = await AuthApiClient.LoginAsync(_usernameOrEmail, _password);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            // Network failure or timeout. Reset the form so the user can retry (audit U-28).
            Logs.Warning($"Login request did not complete: {ex.GetType().Name}");
            _errorMessage = "Could not reach the server. Check your connection and try again.";
            _isSubmitting = false;
            return;
        }

        if (!outcome.Succeeded || outcome.User is null)
        {
            _errorMessage = outcome.Error ?? "Login failed.";
            _isSubmitting = false;
            return;
        }

        AuthStateProvider.NotifyUserAuthenticated(outcome.User);
        Navigation.NavigateTo(ResolveReturnUrl(ReturnUrl));
    }

    /// <summary>
    /// Accepts only same-site absolute paths such as "/datasets/create". Anything else falls back
    /// to the landing page, so a crafted returnUrl cannot send a signed-in user off-site (audit A-8).
    /// "//host" and "/\host" are rejected because browsers treat them as protocol-relative.
    /// </summary>
    public static string ResolveReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl)
            || !returnUrl.StartsWith('/')
            || returnUrl.StartsWith("//", StringComparison.Ordinal)
            || returnUrl.StartsWith("/\\", StringComparison.Ordinal))
        {
            return DefaultLandingPage;
        }

        return returnUrl;
    }
}
