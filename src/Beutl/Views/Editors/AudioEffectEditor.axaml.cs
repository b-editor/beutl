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

        // テンプレートファイルのドロップ
        if (e.DataTransfer.TryGetFile()?.TryGetLocalPath() is { } droppedFile
            && string.Equals(Path.GetExtension(droppedFile), ".json", StringComparison.OrdinalIgnoreCase)
            && ObjectTemplateService.Instance.TryLoadFromFile(droppedFile) is { } template
            && template.CreateInstance() is AudioEffect instance)
        {
            if (viewModel.IsGroup.Value)
            {
                viewModel.AddItem(instance);
            }
            else
            {
                viewModel.ChangeAudioEffect(instance);
            }

            e.Handled = true;
            return;
        }

        if (e.DataTransfer.TryGetValue(BeutlDataFormats.AudioEffect) is not { } data) return;

        if (CoreObjectClipboard.IsJsonData(data))
        {
            if (viewModel.TryPasteJson(data))
            {
                e.Handled = true;
            }
        }
        else if (TypeFormat.ToType(data) is { } type)
        {
            if (viewModel.IsGroup.Value)
            {
                viewModel.AddItem(type);
            }
            else
            {
                viewModel.ChangeFilterType(type);
            }

            e.Handled = true;
        }
    }

    private void DragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(BeutlDataFormats.AudioEffect)
            || e.DataTransfer.Contains(DataFormat.File))
        {
            e.DragEffects = DragDropEffects.Copy | DragDropEffects.Link;
            e.Handled = true;
        }
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
            var dialog = new LibraryItemPickerFlyout(viewModel);
            dialog.ShowAt(this);
            var tcs = new TaskCompletionSource<Type?>();
            dialog.Pinned += (_, item) => viewModel.Pin(item);
            dialog.Unpinned += (_, item) => viewModel.Unpin(item);
            dialog.Dismissed += (_, _) => tcs.SetResult(null);
            dialog.Confirmed += (_, _) =>
            {
                switch (viewModel.SelectedItem.Value?.UserData)
                {
                    case SingleTypeLibraryItem single:
                        tcs.SetResult(single.ImplementationType);
                        break;
                    case MultipleTypeLibraryItem multi:
                        tcs.SetResult(multi.Types.GetValueOrDefault(KnownLibraryItemFormats.AudioEffect));
                        break;
                    default:
                        tcs.SetResult(null);
                        break;
                }
            };

            return await tcs.Task;
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
