using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.Views;
using Beutl.Language;
using Beutl.Models;
using Beutl.Services;
using Beutl.ViewModels.Dialogs;
using Beutl.ViewModels.Editors;

namespace Beutl.Views.Editors;

public partial class AudioEffectEditor : UserControl
{
    private static readonly CrossFade s_transition = new(TimeSpan.FromMilliseconds(250));

    private CancellationTokenSource? _lastTransitionCts;
    private FallbackObjectView? _fallbackObjectView;
    private bool _flyoutOpen;

    public AudioEffectEditor()
    {
        InitializeComponent();
        expandToggle.GetObservable(ToggleButton.IsCheckedProperty)
            .Subscribe(async v =>
            {
                _lastTransitionCts?.Cancel();
                _lastTransitionCts = new CancellationTokenSource();
                CancellationToken localToken = _lastTransitionCts.Token;

                if (v == true)
                {
                    await s_transition.Start(null, content, localToken);
                }
                else
                {
                    await s_transition.Start(content, null, localToken);
                }
            });

        this.GetObservable(DataContextProperty)
            .Select(x => x as AudioEffectEditorViewModel)
            .Select(x => x?.IsFallback.Select(_ => x) ?? Observable.ReturnThenNever<AudioEffectEditorViewModel?>(null))
            .Switch()
            .Where(v => v?.IsFallback.Value == true)
            .Take(1)
            .Subscribe(_ =>
            {
                _fallbackObjectView = new FallbackObjectView();
                content.Children.Add(_fallbackObjectView);
            });

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, DragOver);
        AddHandler(DragDrop.DropEvent, Drop);
    }

    private void Drop(object? sender, DragEventArgs e)
    {
        if (DataContext is not AudioEffectEditorViewModel { IsDisposed: false } viewModel) return;
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
        if (e.DataTransfer.Contains(BeutlDataFormats.AudioEffect))
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

    private async void CopyClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BaseEditorViewModel { IsDisposed: false } vm) return;
        try
        {
            await vm.CopyAsync();
        }
        catch (Exception ex)
        {
            NotificationService.ShowError(Strings.Error, ex.Message);
        }
    }

    private async void PasteClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BaseEditorViewModel { IsDisposed: false } vm) return;
        try
        {
            if (!await vm.PasteAsync())
            {
                NotificationService.ShowInformation(Strings.Paste, MessageStrings.CannotPasteFromClipboard);
            }
        }
        catch (Exception ex)
        {
            NotificationService.ShowError(Strings.Error, ex.Message);
        }
    }

    private async void CopyPasteFlyout_Opening(object? sender, EventArgs e)
    {
        if (DataContext is BaseEditorViewModel { IsDisposed: false } vm)
        {
            await vm.RefreshCanPasteAsync();
        }
    }
}
