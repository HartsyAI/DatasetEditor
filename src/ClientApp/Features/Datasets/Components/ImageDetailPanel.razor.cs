using System;
using Microsoft.AspNetCore.Components;
using DatasetStudio.ClientApp.Shared.Services;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;
using DatasetStudio.ClientApp.Features.Datasets.Components;
using DatasetStudio.ClientApp.Features.Datasets.Services;
using DatasetStudio.ClientApp.Services.StateManagement;
using DatasetStudio.Core.DomainModels;
using DatasetStudio.Core.DomainModels.Items;
using DatasetStudio.Core.Utilities;
using DatasetStudio.DTO.Items;
using DatasetStudio.DTO.Datasets;

namespace DatasetStudio.ClientApp.Features.Datasets.Components;

/// <summary>Detail panel for viewing and editing image metadata</summary>
public partial class ImageDetailPanel
{
    [Inject] public DatasetState DatasetState { get; set; } = default!;
    [Inject] public ItemEditService EditService { get; set; } = default!;
    [Inject] public IDialogService DialogService { get; set; } = default!;
    [Inject] public NotificationService Notifications { get; set; } = default!;
    [Inject] public ImageUrlHelper ImageUrlHelper { get; set; } = default!;

    [Parameter] public DatasetItemDto? Item { get; set; }

    private string ResolvedImageUrl => Item != null ? ImageUrlHelper.ResolveImageUrl(Item.ImageUrl) : string.Empty;

    private bool _isEditingTitle = false;
    private bool _isEditingDescription = false;
    private string _editTitle = string.Empty;
    private string _editDescription = string.Empty;

    protected override void OnParametersSet()
    {
        if (Item != null)
        {
            _editTitle = Item.Title;
            _editDescription = Item.Description ?? string.Empty;
        }
    }

    public void StartEditTitle()
    {
        _isEditingTitle = true;
        _editTitle = Item?.Title ?? string.Empty;
    }

    public async Task SaveTitle()
    {
        if (Item == null) return;
        
        _isEditingTitle = false;
        
        if (_editTitle != Item.Title)
        {
            bool success = await EditService.UpdateItemAsync(Item, title: _editTitle);
            
            if (success)
            {
                Notifications.ShowSuccess("Title updated");
            }
            else
            {
                Notifications.ShowError("Failed to update title");
            }
        }
    }

    public async Task HandleTitleKeyUp(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            await SaveTitle();
        }
        else if (e.Key == "Escape")
        {
            _isEditingTitle = false;
            _editTitle = Item?.Title ?? string.Empty;
        }
    }

    public void StartEditDescription()
    {
        _isEditingDescription = true;
        _editDescription = Item?.Description ?? string.Empty;
    }

    public async Task SaveDescription()
    {
        if (Item == null) return;
        
        _isEditingDescription = false;
        
        if (_editDescription != Item.Description)
        {
            bool success = await EditService.UpdateItemAsync(Item, description: _editDescription);
            
            if (success)
            {
                Notifications.ShowSuccess("Description updated");
            }
            else
            {
                Notifications.ShowError("Failed to update description");
            }
        }
    }

    public async Task RemoveTag(string tag)
    {
        if (Item == null) return;
        
        bool success = await EditService.RemoveTagAsync(Item, tag);
        
        if (success)
        {
            Notifications.ShowSuccess($"Tag '{tag}' removed");
        }
        else
        {
            Notifications.ShowError("Failed to remove tag");
        }
    }

    public async Task ShowAddTagDialog()
    {
        if (Item == null) return;
        
        DialogOptions options = new() { MaxWidth = MaxWidth.Small, FullWidth = true };
        
        Type addTagDialogType = typeof(AddTagDialog);
        IDialogReference? dialog = DialogService.Show(addTagDialogType, "Add Tag", options);
        DialogResult? result = await dialog.Result;
        
        if (result != null && !result.Canceled && result.Data is string newTag)
        {
            bool success = await EditService.AddTagAsync(Item, newTag);
            
            if (success)
            {
                Notifications.ShowSuccess($"Tag '{newTag}' added");
            }
            else
            {
                Notifications.ShowError("Failed to add tag");
            }
        }
    }

    /// <summary>Opens the lightbox from the keyboard: Enter or Space on the focused preview.</summary>
    private Task HandleImageKeyDown(KeyboardEventArgs args)
    {
        return args.Key is "Enter" or " " ? OpenLightboxAsync() : Task.CompletedTask;
    }

    /// <summary>Opens the full-size lightbox (a dialog: Escape closes it and it traps focus).</summary>
    public async Task OpenLightboxAsync()
    {
        if (Item is null)
        {
            return;
        }

        var parameters = new DialogParameters
        {
            { "Item", Item }
        };

        var options = new DialogOptions
        {
            MaxWidth = MaxWidth.ExtraLarge,
            FullWidth = true,
            CloseButton = true,
            CloseOnEscapeKey = true
        };

        await DialogService.ShowAsync<ImageLightbox>(Item.Title ?? "Image", parameters, options);
    }
}
