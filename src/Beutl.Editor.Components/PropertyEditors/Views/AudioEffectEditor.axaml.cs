using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Beutl.Audio.Effects;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Services;
using FluentAvalonia.UI.Controls;

namespace Beutl.Editor.Components.PropertyEditors.Views;

public partial class AudioEffectEditor : UserControl
{
    public AudioEffectEditor()
    {
        InitializeComponent();
        ExpandTransitionHelper.Attach(expandToggle, content);
        FallbackObjectViewHelper.Attach(this, view => content.Children.Add(view));

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, DragOver);
        AddHandler(DragDrop.DropEvent, Drop);

        EditorMenuHelper.AttachRepairMenus(
            this,
            (FAMenuFlyout)expandToggle.ContextFlyout!,
            () => DataContext is AudioEffectEditorViewModel { NeedsRepair: true });
    }

    private void Drop(object? sender, DragEventArgs e)
    {
        if (DataContext is not AudioEffectEditorViewModel { IsDisposed: false } viewModel) return;

        if (EditorDragDropHelper.TryHandleEditorDrop<AudioEffect>(
                e,
                BeutlDataFormats.AudioEffect,
                tryPasteJson: viewModel.TryPasteJson,
                onTemplateInstance: instance => viewModel.AddItem(instance),
                onTypePayload: type =>
                {
                    viewModel.AddItem(type);
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

        Type? type = await SelectType(sender as Control);
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

    private Task<Type?> SelectType(Control? button)
    {
        return PickerGuard.RunAsync(this, button, async () =>
        {
            var viewModel = new SelectLibraryItemDialogViewModel(KnownLibraryItemFormats.AudioEffect, typeof(AudioEffect));
            return await LibraryItemPickerHelper.ShowAsync(this, viewModel, KnownLibraryItemFormats.AudioEffect) as Type;
        });
    }

    private async void ChangeEffectTypeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AudioEffectEditorViewModel { IsDisposed: false } viewModel) return;

        Type? type = await SelectType(null);
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
