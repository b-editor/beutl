using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Services;
using Beutl.ViewModels;
using Beutl.ViewModels.Dialogs;
using Beutl.ViewModels.Editors;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Views.Editors;

public partial class TextureSourceEditor : UserControl
{
    private bool _flyoutOpen;

    public TextureSourceEditor()
    {
        InitializeComponent();
        ExpandTransitionHelper.Attach(expandToggle, content);
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

            return await LibraryItemPickerHelper.ShowAsync(this, selectVm, KnownLibraryItemFormats.Drawable);
        }
        finally
        {
            _flyoutOpen = false;
        }
    }
}
