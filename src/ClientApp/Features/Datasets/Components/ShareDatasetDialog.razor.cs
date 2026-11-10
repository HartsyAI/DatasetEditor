using DatasetStudio.ClientApp.Services.ApiClients;
using DatasetStudio.ClientApp.Shared.Services;
using DatasetStudio.Core.Utilities.Logging;
using DatasetStudio.DTO.Datasets;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace DatasetStudio.ClientApp.Features.Datasets.Components;

/// <summary>
/// Shares a dataset with other accounts, changes or ends their shares, and lists who has one.
/// Before this, the server had grant checks but no way to create a grant, so a private dataset
/// could only ever be seen by its owner (audit D-2). Refusals show the server's own reason.
/// </summary>
public partial class ShareDatasetDialog : ComponentBase
{
    [CascadingParameter] private MudDialogInstance Dialog { get; set; } = default!;
    [Inject] private DatasetApiClient Api { get; set; } = default!;
    [Inject] private NotificationService Notifications { get; set; } = default!;

    /// <summary>The dataset being shared.</summary>
    [Parameter, EditorRequired] public Guid DatasetId { get; set; }

    /// <summary>Its name, for the title.</summary>
    [Parameter] public string DatasetName { get; set; } = string.Empty;

    private List<DatasetShareDto> _shares = new();
    private bool _isLoading = true;
    private string? _loadError;

    private string _who = string.Empty;
    private DatasetAccessLevel _accessLevel = DatasetAccessLevel.Read;
    private bool _canDelete;
    private bool _canShare;
    private DateTime? _expiresOn;
    private bool _isSaving;
    private string? _formError;
    private Guid? _revoking;

    private readonly ShareFormState _form = new();

    private bool CanSubmit => !_isSaving && !string.IsNullOrWhiteSpace(_who);

    /// <summary>Loads the current shares.</summary>
    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        _isLoading = true;
        _loadError = null;
        try
        {
            _shares = await Api.GetSharesAsync(DatasetId);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        {
            Logs.Warning($"Loading shares failed: {ex.Message}");
            _loadError = "The server didn't return the list of shares. Try again.";
        }
        _isLoading = false;
    }

    private async Task ShareAsync()
    {
        if (!CanSubmit)
        {
            return;
        }
        _isSaving = true;
        _formError = null;
        ShareDatasetRequest request = _form.ToRequest(_who, _accessLevel, _canDelete, _canShare, _expiresOn);
        try
        {
            (DatasetShareDto? share, string? error) = await Api.ShareDatasetAsync(DatasetId, request);
            if (share is null)
            {
                _formError = error;
                return;
            }
            _shares = _shares.Where(s => s.UserId != share.UserId).Append(share).OrderBy(s => s.Username, StringComparer.OrdinalIgnoreCase).ToList();
            Notifications.ShowSuccess($"Shared with {share.Username}.");
            ResetForm();
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            Logs.Warning($"Sharing failed: {ex.Message}");
            _formError = "Couldn't reach the server. Try again.";
        }
        finally
        {
            _isSaving = false;
        }
    }

    /// <summary>Puts a share's current settings in the form, to change them.</summary>
    private void Edit(DatasetShareDto share)
    {
        _who = share.Username;
        _accessLevel = share.AccessLevel;
        _canDelete = share.CanDelete;
        _canShare = share.CanShare;
        _expiresOn = _form.BeginEdit(share);
        _formError = null;
    }

    private async Task RevokeAsync(DatasetShareDto share)
    {
        _revoking = share.UserId;
        try
        {
            if (await Api.RevokeShareAsync(DatasetId, share.UserId))
            {
                _shares = _shares.Where(s => s.UserId != share.UserId).ToList();
                Notifications.ShowSuccess($"Stopped sharing with {share.Username}.");
            }
            else
            {
                Notifications.ShowError($"Couldn't stop sharing with {share.Username}. Someone else may have shared it with them.");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            Logs.Warning($"Revoking a share failed: {ex.Message}");
            Notifications.ShowError("Couldn't reach the server. Try again.");
        }
        finally
        {
            _revoking = null;
        }
    }

    /// <summary>The line under a name: username, extra rights, end date.</summary>
    private static string Describe(DatasetShareDto share)
    {
        List<string> parts = new() { share.Username };
        if (share.CanDelete)
        {
            parts.Add("can delete");
        }
        if (share.CanShare)
        {
            parts.Add("can share");
        }
        if (share.ExpiresAt is DateTime expires)
        {
            parts.Add(expires <= DateTime.UtcNow ? "ended" : $"until {expires.ToLocalTime():d MMM yyyy}");
        }
        return string.Join(" · ", parts);
    }

    private void ResetForm()
    {
        _who = string.Empty;
        _accessLevel = DatasetAccessLevel.Read;
        _canDelete = false;
        _canShare = false;
        _expiresOn = null;
        _form.Clear();
    }

    private void Close() => Dialog.Close();
}
