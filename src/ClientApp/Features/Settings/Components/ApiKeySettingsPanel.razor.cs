using Blazored.LocalStorage;
using DatasetStudio.ClientApp.Services.StateManagement;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace DatasetStudio.ClientApp.Features.Settings.Components;

/// <summary>
/// The HuggingFace access token, saved to LocalStorage when the field loses focus. The panel used
/// to offer a "Hartsy" API key that nothing read, and said keys were "never sent to servers",
/// which was wrong: the token goes to the API with HuggingFace item requests (X-HF-Access-Token),
/// which forwards it upstream.
/// </summary>
public partial class ApiKeySettingsPanel
{
    [Inject] private ApiKeyState ApiKeyState { get; set; } = default!;
    [Inject] private ILocalStorageService LocalStorage { get; set; } = default!;

    private string? _huggingFaceToken;

    /// <summary>Loads the saved token into the field.</summary>
    protected override async Task OnInitializedAsync()
    {
        await ApiKeyState.LoadFromStorageAsync(LocalStorage);
        _huggingFaceToken = ApiKeyState.GetToken(ApiKeyState.ProviderHuggingFace);
    }

    private async Task SaveHuggingFaceTokenAsync(FocusEventArgs _)
    {
        ApiKeyState.SetToken(ApiKeyState.ProviderHuggingFace, _huggingFaceToken);
        await ApiKeyState.SaveToStorageAsync(LocalStorage);
    }
}
