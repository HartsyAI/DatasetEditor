using DatasetStudio.ClientApp.Features.Auth.Services;
using Microsoft.AspNetCore.Components;

namespace DatasetStudio.ClientApp.Features.Auth.Pages;

public partial class Register : ComponentBase
{
    [Inject] private AuthApiClient AuthApiClient { get; set; } = default!;
    [Inject] private CookieAuthenticationStateProvider AuthStateProvider { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    private string _username = string.Empty;
    private string _email = string.Empty;
    private string _password = string.Empty;
    private string? _errorMessage;
    private bool _isSubmitting;

    private async Task SubmitAsync()
    {
        if (_isSubmitting || string.IsNullOrWhiteSpace(_username) || string.IsNullOrWhiteSpace(_email) || string.IsNullOrWhiteSpace(_password))
        {
            return;
        }

        _isSubmitting = true;
        _errorMessage = null;

        // Register auto-logs-in on success (see AuthEndpoints.Register), so no separate login
        // round trip is needed here.
        AuthOutcome outcome = await AuthApiClient.RegisterAsync(_username, _email, _password, displayName: null);
        if (!outcome.Succeeded || outcome.User is null)
        {
            _errorMessage = outcome.Error ?? "Registration failed.";
            _isSubmitting = false;
            return;
        }

        AuthStateProvider.NotifyUserAuthenticated(outcome.User);
        Navigation.NavigateTo("/my-datasets");
    }
}
