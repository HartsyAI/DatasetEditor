using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Blazored.LocalStorage;
using DatasetStudio.Core.Constants;
using DatasetStudio.Core.DomainModels;
using DatasetStudio.Core.Utilities;
using DatasetStudio.Core.Utilities.Logging;

namespace DatasetStudio.ClientApp.Services.StateManagement;

public sealed class ApiKeyState
{
    public const string ProviderHuggingFace = "huggingface";
    public const string ProviderHartsy = "hartsy";

    public ApiKeySettings Settings { get; private set; } = new ApiKeySettings();

    public event Action? OnChange;

    public string? GetToken(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return null;
        }

        string key = providerId.Trim();

        if (Settings.Tokens.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return null;
    }

    public void SetToken(string providerId, string? token)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return;
        }

        string key = providerId.Trim();

        if (string.IsNullOrWhiteSpace(token))
        {
            if (Settings.Tokens.Remove(key))
            {
                NotifyStateChanged();
            }

            return;
        }

        Settings.Tokens[key] = token;
        NotifyStateChanged();
    }

    public void ClearAllTokens()
    {
        if (Settings.Tokens.Count == 0)
        {
            return;
        }

        Settings.Tokens = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        NotifyStateChanged();
    }

    public async Task LoadFromStorageAsync(ILocalStorageService storage)
    {
        try
        {
            ApiKeySettings? saved = await storage.GetItemAsync<ApiKeySettings>(StorageKeys.ApiKeys);
            if (saved != null)
            {
                Settings = saved;
                NotifyStateChanged();
                Logs.Info("API key settings loaded from LocalStorage");
            }
        }
        catch (Exception ex)
        {
            Logs.Error("Failed to load API key settings from LocalStorage", ex);
        }
    }

    public async Task SaveToStorageAsync(ILocalStorageService storage)
    {
        try
        {
            await storage.SetItemAsync(StorageKeys.ApiKeys, Settings);
            Logs.Info("API key settings saved to LocalStorage");
        }
        catch (Exception ex)
        {
            Logs.Error("Failed to save API key settings to LocalStorage", ex);
        }
    }

    private void NotifyStateChanged()
    {
        OnChange?.Invoke();
    }
}
