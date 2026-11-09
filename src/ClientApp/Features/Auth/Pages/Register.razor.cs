using DatasetStudio.ClientApp.Features.Auth.Services;
using DatasetStudio.Core.Utilities.Logging;
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
        AuthOutcome outcome;
        try
        {
            outcome = await AuthApiClient.RegisterAsync(_username, _email, _password, displayName: null);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            // Network failure or timeout. Reset the form so the user can retry (audit U-28).
            Logs.Warning($"Registration request did not complete: {ex.GetType().Name}");
            _errorMessage = "Could not reach the server. Check your connection and try again.";
            _isSubmitting = false;
            return;
        }

        if (!outcome.Succeeded || outcome.User is null)
        {
            _errorMessage = outcome.Error ?? "Registration failed.";
            _isSubmitting = false;
            return;
        }

        AuthStateProvider.NotifyUserAuthenticated(outcome.User);
        Navigation.NavigateTo("/datasets");
    }
}
