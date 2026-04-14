using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Beutl.Audio.Effects;
using Beutl.Editor.Components.Helpers;
using Beutl.Services;
using Beutl.ViewModels.Dialogs;
using Beutl.ViewModels.Editors;
using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Editors;

public partial class AudioEffectEditor : UserControl
{
    private bool _flyoutOpen;

    public AudioEffectEditor()
    {
        InitializeComponent();
        ExpandTransitionHelper.Attach(expandToggle, content);
        FallbackObjectViewHelper.Attach(this, view => content.Children.Add(view));

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, DragOver);
        AddHandler(DragDrop.DropEvent, Drop);

        EditorMenuHelper.AttachCopyPasteAndTemplateMenus(this, (FAMenuFlyout)expandToggle.ContextFlyout!);
    }

    private void Drop(object? sender, DragEventArgs e)
    {
        if (DataContext is not AudioEffectEditorViewModel { IsDisposed: false } viewModel) return;

        if (EditorDragDropHelper.TryHandleEditorDrop<AudioEffect>(
                e,
                BeutlDataFormats.AudioEffect,
                tryPasteJson: viewModel.TryPasteJson,
                onTemplateInstance: instance =>
                {
                    if (viewModel.IsGroup.Value)
                        viewModel.AddItem(instance);
                    else
                        viewModel.ChangeAudioEffect(instance);
                },
                onTypePayload: type =>
                {
                    if (viewModel.IsGroup.Value)
                        viewModel.AddItem(type);
                    else
                        viewModel.ChangeFilterType(type);
                    return true;
                }))
        {
            e.Handled = true;
        }
    }

    private void DragOver(object? sender, DragEventArgs e)
    {
        EditorDragDropHelper.HandleEditorDragOver(e, BeutlDataFormats.AudioEffect);
    }

    private async void Tag_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AudioEffectEditorViewModel { IsDisposed: false } viewModel) return;

        if (viewModel.IsGroup.Value)
        {
            Type? type = await SelectType();
            if (type != null)
            {
                try
                {
                    viewModel.AddItem(type);
                }
                catch (Exception ex)
                {
                    NotificationService.ShowError(Strings.Error, ex.Message);
                }
            }
        }
        else
        {
            expandToggle.ContextFlyout?.ShowAt(expandToggle);
        }
    }

    private async Task<Type?> SelectType()
    {
        if (_flyoutOpen) return null;

        try
        {
            _flyoutOpen = true;
            var viewModel = new SelectAudioEffectTypeViewModel();
            return await LibraryItemPickerHelper.ShowTypeOnlyAsync(this, viewModel, KnownLibraryItemFormats.AudioEffect);
        }
        finally
        {
            _flyoutOpen = false;
        }
    }

    private async void ChangeEffectTypeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AudioEffectEditorViewModel { IsDisposed: false } viewModel) return;

        Type? type = await SelectType();
        if (type != null)
        {
            try
            {
                viewModel.ChangeFilterType(type);
            }
            catch (Exception ex)
            {
                NotificationService.ShowError(Strings.Error, ex.Message);
            }
        }
    }

    private void SetNullClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AudioEffectEditorViewModel { IsDisposed: false } viewModel)
        {
            viewModel.SetNull();
        }
    }
}
