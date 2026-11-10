using System;
using DatasetStudio.ClientApp.Features.Datasets.Components;
using DatasetStudio.DTO.Datasets;
using Xunit;

namespace DatasetStudio.Tests.ClientApp;

/// <summary>
/// PR 5.2 review: editing a share must keep targeting that account (by id) and keep its end
/// instant unless the date is changed.
/// </summary>
public sealed class ShareFormStateTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    /// <summary>An end that isn't local midnight used to move by up to a day on any edit.</summary>
    [Fact]
    public void Edit_KeepsTheEndInstant_WhenTheDateIsUnchanged()
    {
        DateTime end = new DateTime(2030, 5, 6, 15, 45, 0, DateTimeKind.Utc);
        ShareFormState form = new();
        DateTime? shown = form.BeginEdit(new DatasetShareDto { UserId = UserId, Username = "bob", ExpiresAt = end });

        ShareDatasetRequest request = form.ToRequest("bob", DatasetAccessLevel.Write, false, false, shown);

        Assert.Equal(end, request.ExpiresAt);
        Assert.Equal(end.ToLocalTime().Date, shown);
    }

    /// <summary>A changed date means the end of that local day.</summary>
    [Fact]
    public void Edit_UsesTheNewDate_WhenItChanged()
    {
        ShareFormState form = new();
        form.BeginEdit(new DatasetShareDto { UserId = UserId, Username = "bob", ExpiresAt = new DateTime(2030, 5, 6, 15, 45, 0, DateTimeKind.Utc) });
        DateTime picked = new(2030, 6, 1);

        ShareDatasetRequest request = form.ToRequest("bob", DatasetAccessLevel.Read, false, false, picked);

        Assert.Equal(picked.AddDays(1).ToUniversalTime(), request.ExpiresAt);
    }

    /// <summary>An edit names the account by id; typing another name makes it a new share.</summary>
    [Fact]
    public void Edit_TargetsTheAccountById_UntilTheNameChanges()
    {
        ShareFormState form = new();
        form.BeginEdit(new DatasetShareDto { UserId = UserId, Username = "pat@example.com" });

        Assert.Equal(UserId, form.ToRequest("pat@example.com", DatasetAccessLevel.Read, false, false, null).UserId);
        Assert.Null(form.ToRequest("someone-else", DatasetAccessLevel.Read, false, false, null).UserId);

        form.Clear();
        Assert.Null(form.ToRequest("pat@example.com", DatasetAccessLevel.Read, false, false, null).UserId);
    }

    /// <summary>A share ending at local midnight shows the day before (the form's end-of-day convention).</summary>
    [Fact]
    public void PickerDate_ForAMidnightEnd_IsTheDayBefore()
    {
        DateTime localMidnight = new DateTime(2030, 5, 7, 0, 0, 0, DateTimeKind.Local);
        Assert.Equal(new DateTime(2030, 5, 6), ShareFormState.ToPickerDate(localMidnight.ToUniversalTime()));
    }
}
