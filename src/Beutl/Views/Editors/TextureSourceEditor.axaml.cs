using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Beutl.Controls.PropertyEditors;
using Beutl.Graphics;
using Beutl.Graphics3D.Textures;
using Beutl.Media.Source;
using Beutl.Services;
using Beutl.ViewModels.Dialogs;
using Beutl.ViewModels.Editors;
using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Editors;

public partial class TextureSourceEditor : UserControl
{
    private static readonly CrossFade s_transition = new(TimeSpan.FromMilliseconds(250));

    private CancellationTokenSource? _lastTransitionCts;
    private bool _flyoutOpen;

    public TextureSourceEditor()
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
    }

    private void Menu_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            button.ContextFlyout?.ShowAt(button);
        }
    }

    private void ChangeTextureSourceType(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TextureSourceEditorViewModel { IsDisposed: false } vm) return;
        if (sender is not RadioMenuFlyoutItem { Tag: string tag }) return;

        switch (tag)
        {
            case "Image":
                vm.ChangeToImageTextureSource();
                break;
            case "Drawable":
                vm.ChangeToDrawableTextureSource();
                break;
            case "Null":
                vm.ChangeToNull();
                break;
        }
    }

    private async void SelectDrawable_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TextureSourceEditorViewModel { IsDisposed: false } vm) return;

        Type? type = await SelectDrawableType();
        if (type != null)
        {
            vm.SetDrawableType(type);
        }
    }

    private async Task<Type?> SelectDrawableType()
    {
        if (_flyoutOpen) return null;

        try
        {
            _flyoutOpen = true;
            var viewModel = new SelectDrawableTypeViewModel();
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
                        tcs.SetResult(multi.Types.GetValueOrDefault(KnownLibraryItemFormats.Drawable));
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
}
