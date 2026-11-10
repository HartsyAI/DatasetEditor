using DatasetStudio.DTO.Datasets;

namespace DatasetStudio.ClientApp.Features.Datasets.Components;

/// <summary>
/// What the share dialog's form is editing: a new share, or an existing one (its account id and
/// end instant), and how that becomes a request. Kept apart from the component so the rules can
/// be tested without rendering it.
/// </summary>
public sealed class ShareFormState
{
    private DatasetShareDto? _editing;
    private DateTime? _editingPickerDate;

    /// <summary>The share being changed, or null for a new share.</summary>
    public DatasetShareDto? Editing => _editing;

    /// <summary>Starts editing a share; returns the date to show in the picker.</summary>
    public DateTime? BeginEdit(DatasetShareDto share)
    {
        _editing = share;
        _editingPickerDate = ToPickerDate(share.ExpiresAt);
        return _editingPickerDate;
    }

    /// <summary>Back to adding a new share.</summary>
    public void Clear()
    {
        _editing = null;
        _editingPickerDate = null;
    }

    /// <summary>
    /// The request for the form's values. An edit targets the share's account by id while the
    /// name field still shows it: re-resolving the name could reach another account whose email
    /// equals this username. The end instant is kept unless the date was changed; turning it into
    /// a date and back moved a non-midnight end by up to a day.
    /// </summary>
    public ShareDatasetRequest ToRequest(string who, DatasetAccessLevel accessLevel, bool canDelete, bool canShare, DateTime? pickerDate)
    {
        bool sameAccount = _editing is not null
            && string.Equals(who.Trim(), _editing.Username, StringComparison.OrdinalIgnoreCase);
        DateTime? expiresAt = sameAccount && pickerDate == _editingPickerDate
            ? _editing!.ExpiresAt
            : FromPickerDate(pickerDate);
        return new ShareDatasetRequest(who.Trim(), accessLevel, canDelete, canShare, expiresAt)
        {
            UserId = sameAccount ? _editing!.UserId : null
        };
    }

    /// <summary>The picker's local date means the share runs to the end of that day.</summary>
    public static DateTime? FromPickerDate(DateTime? pickerDate) =>
        pickerDate?.Date.AddDays(1).ToUniversalTime();

    /// <summary>
    /// The local day a share ends on: the day before when it ends exactly at local midnight (the
    /// form's own end-of-day convention), otherwise the day it ends.
    /// </summary>
    public static DateTime? ToPickerDate(DateTime? expiresAt)
    {
        if (expiresAt is not DateTime end)
        {
            return null;
        }
        DateTime local = end.ToLocalTime();
        return local.TimeOfDay == TimeSpan.Zero ? local.Date.AddDays(-1) : local.Date;
    }
}
