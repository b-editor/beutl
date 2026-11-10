using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.LibraryThumbnails;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Engine;
using Beutl.Graphics.Effects;
using Beutl.Models;
using Beutl.Services;
using FluentAvalonia.UI.Controls;

namespace Beutl.Editor.Components.PropertyEditors.Views;

public partial class FilterEffectEditor : UserControl
{
    public FilterEffectEditor()
    {
        InitializeComponent();
        ExpandTransitionHelper.Attach(expandToggle, content);

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, DragOver);
        AddHandler(DragDrop.DropEvent, Drop);

        FallbackObjectViewHelper.Attach(this, view => content.Children.Add(view));

        EditorMenuHelper.AttachRepairMenus(
            this,
            (FAMenuFlyout)expandToggle.ContextFlyout!,
            () => DataContext is FilterEffectEditorViewModel { NeedsRepair: true });
        EditorMenuHelper.AttachCopyPasteAndTemplateMenus(this, (FAMenuFlyout)ReferenceMenuButton.Flyout!);
    }

    private void Drop(object? sender, DragEventArgs e)
    {
        if (DataContext is not FilterEffectEditorViewModel { IsDisposed: false } viewModel) return;

        if (EditorDragDropHelper.TryHandleEditorDrop<FilterEffect>(
                e,
                BeutlDataFormats.FilterEffect,
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
        EditorDragDropHelper.HandleEditorDragOver(e, BeutlDataFormats.FilterEffect);
    }

    private async void Tag_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FilterEffectEditorViewModel { IsDisposed: false } viewModel) return;

        object? result = await SelectTypeOrReference(sender as Control);

        try
        {
            switch (result)
            {
                case Type type:
                    viewModel.AddItem(type);
                    break;
                case FilterEffect fe:
                    Type? presenterType = PresenterTypeAttribute.GetPresenterType(typeof(FilterEffect));
                    if (presenterType != null)
                    {
                        viewModel.AddTarget(presenterType, fe);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            NotificationService.ShowError(Strings.Error, ex.Message);
        }
    }

    private Task<object?> SelectTypeOrReference(Control? button)
    {
        return PickerGuard.RunAsync(this, button, () =>
        {
            var selectVm = new SelectLibraryItemDialogViewModel(
                KnownLibraryItemFormats.FilterEffect,
                typeof(FilterEffect),
                LibraryThumbnailCapture.ForObject(DataContext as IServiceProvider));

            if (DataContext is FilterEffectEditorViewModel { IsDisposed: false } vm
                && PresenterTypeAttribute.GetPresenterType(typeof(FilterEffect)) != null)
            {
                var targets = vm.GetAvailableTargets();
                selectVm.InitializeReferences(targets);
            }

            return LibraryItemPickerHelper.ShowAsync(this, selectVm, KnownLibraryItemFormats.FilterEffect);
        });
    }

    private async void ChangeFilterTypeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FilterEffectEditorViewModel { IsDisposed: false } viewModel) return;

        object? result = await SelectTypeOrReference(null);

        try
        {
            switch (result)
            {
                case Type type:
                    viewModel.ChangeFilterType(type);
                    break;
                case FilterEffect target:
                    Type? presenterType = PresenterTypeAttribute.GetPresenterType(typeof(FilterEffect));
                    if (presenterType != null)
                    {
                        viewModel.ChangeFilterType(presenterType);
                        viewModel.SetTarget(target);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            NotificationService.ShowError(Strings.Error, ex.Message);
        }
    }

    private void SetNullClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FilterEffectEditorViewModel { IsDisposed: false } viewModel) return;

        viewModel.SetNull();
    }

    private async void SelectTarget_Requested(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FilterEffectEditorViewModel { IsDisposed: false } vm) return;

        await TargetSelectionHelper.PickTargetAsync(this, sender as Control, vm);
    }
}
