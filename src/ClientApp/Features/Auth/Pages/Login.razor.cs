using DatasetStudio.ClientApp.Features.Auth.Services;
using Microsoft.AspNetCore.Components;

namespace DatasetStudio.ClientApp.Features.Auth.Pages;

public partial class Login : ComponentBase
{
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

    private async Task SubmitAsync()
    {
        if (_isSubmitting || string.IsNullOrWhiteSpace(_usernameOrEmail) || string.IsNullOrWhiteSpace(_password))
        {
            return;
        }

        _isSubmitting = true;
        _errorMessage = null;

        AuthOutcome outcome = await AuthApiClient.LoginAsync(_usernameOrEmail, _password);
        if (!outcome.Succeeded || outcome.User is null)
        {
            _errorMessage = outcome.Error ?? "Login failed.";
            _isSubmitting = false;
            return;
        }

        AuthStateProvider.NotifyUserAuthenticated(outcome.User);
        Navigation.NavigateTo(string.IsNullOrWhiteSpace(ReturnUrl) ? "/my-datasets" : ReturnUrl);
    }
}
