using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace DatasetStudio.ClientApp.Shared.Components;

/// <summary>
/// Confirmation for destructive or irreversible actions (DESIGN.md §4.1, §4.6). The answer is
/// returned only through the dialog result (use <see cref="DialogServiceExtensions.ConfirmAsync"/>);
/// the earlier version also took confirm/cancel callbacks, which DESIGN.md rules out, and was used
/// nowhere while dataset delete used a generic message box.
/// </summary>
public partial class ConfirmDialog
{
    [CascadingParameter] private MudDialogInstance Dialog { get; set; } = default!;

    /// <summary>Dialog title.</summary>
    [Parameter] public string Title { get; set; } = "Confirm action";

    /// <summary>The question, naming what will happen.</summary>
    [Parameter] public string Message { get; set; } = "Are you sure you want to continue?";

    /// <summary>Optional consequence line, for example "This cannot be undone."</summary>
    [Parameter] public string? SecondaryMessage { get; set; }

    /// <summary>Confirm button label; a verb naming the action ("Delete").</summary>
    [Parameter] public string ConfirmLabel { get; set; } = "Confirm";

    /// <summary>Cancel button label.</summary>
    [Parameter] public string CancelLabel { get; set; } = "Cancel";

    /// <summary>Styles the confirm button <c>Filled Error</c> for deletes and other destructive actions.</summary>
    [Parameter] public bool Destructive { get; set; }

    private void Confirm() => Dialog.Close(DialogResult.Ok(true));

    private void Cancel() => Dialog.Cancel();
}
