using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Beutl.Controls.PropertyEditors;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics3D.Textures;
using Beutl.Media.Source;
using Beutl.Services;
using Beutl.ViewModels;
using Beutl.ViewModels.Dialogs;
using Beutl.ViewModels.Editors;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;

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

        object? result = await SelectDrawableTypeOrReference();

        switch (result)
        {
            case Type type:
                vm.SetDrawableType(type);
                break;
            case Drawable target:
                vm.SetDrawableTarget(target);
                break;
        }
    }

    private async Task<object?> SelectDrawableTypeOrReference()
    {
        if (_flyoutOpen) return null;

        try
        {
            _flyoutOpen = true;
            var selectVm = new SelectDrawableTypeViewModel();

            if (DataContext is TextureSourceEditorViewModel { IsDisposed: false } vm
                && PresenterTypeAttribute.GetPresenterType(typeof(Drawable)) != null)
            {
                var scene = vm.GetService<EditViewModel>()?.Scene;
                if (scene != null)
                {
                    var searcher = new ObjectSearcher(scene, obj =>
                        obj is Drawable && obj is not IPresenter<Drawable>);
                    var targets = searcher.SearchAll()
                        .Cast<Drawable>()
                        .Select(d => new TargetObjectInfo(
                            CoreObjectHelper.GetDisplayName(d), d, CoreObjectHelper.GetOwnerElement(d)))
                        .ToList();
                    selectVm.InitializeReferences(targets);
                }
            }

            var dialog = new LibraryItemPickerFlyout(selectVm);
            dialog.ShowAt(this);
            var tcs = new TaskCompletionSource<object?>();
            dialog.Pinned += (_, item) => selectVm.Pin(item);
            dialog.Unpinned += (_, item) => selectVm.Unpin(item);
            dialog.Dismissed += (_, _) => tcs.SetResult(null);
            dialog.Confirmed += (_, _) =>
            {
                switch (selectVm.SelectedItem.Value?.UserData)
                {
                    case TargetObjectInfo target:
                        tcs.SetResult(target.Object);
                        break;
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
