using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Beutl.Editor.Components.Helpers;
using Beutl.Engine;
using Beutl.Graphics.Effects;
using Beutl.Models;
using Beutl.Services;
using Beutl.ViewModels.Dialogs;
using Beutl.ViewModels.Editors;
using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Editors;

public partial class FilterEffectEditor : UserControl
{
    private bool _flyoutOpen;

    public FilterEffectEditor()
    {
        InitializeComponent();
        ExpandTransitionHelper.Attach(expandToggle, content);

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, DragOver);
        AddHandler(DragDrop.DropEvent, Drop);

        FallbackObjectViewHelper.Attach(this, view => content.Children.Add(view));

        EditorMenuHelper.AttachCopyPasteAndTemplateMenus(
            this,
            (FAMenuFlyout)expandToggle.ContextFlyout!,
            (FAMenuFlyout)ReferenceMenuButton.Flyout!);
    }

    private void Drop(object? sender, DragEventArgs e)
    {
        if (DataContext is not FilterEffectEditorViewModel { IsDisposed: false } viewModel) return;

        // テンプレートファイルのドロップ
        if (e.DataTransfer.TryGetFile()?.TryGetLocalPath() is { } droppedFile
            && string.Equals(Path.GetExtension(droppedFile), ".json", StringComparison.OrdinalIgnoreCase)
            && ObjectTemplateService.Instance.TryLoadFromFile(droppedFile) is { } template
            && template.CreateInstance() is FilterEffect instance)
        {
            if (viewModel.IsGroup.Value)
            {
                viewModel.AddItem(instance);
            }
            else
            {
                viewModel.ChangeFilter(instance);
            }

            e.Handled = true;
            return;
        }

        if (e.DataTransfer.TryGetValue(BeutlDataFormats.FilterEffect) is not { } data) return;

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
        if (e.DataTransfer.Contains(BeutlDataFormats.FilterEffect)
            || e.DataTransfer.Contains(DataFormat.File))
        {
            e.DragEffects = DragDropEffects.Copy | DragDropEffects.Link;
            e.Handled = true;
        }
    }

    private async void Tag_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FilterEffectEditorViewModel { IsDisposed: false } viewModel) return;

        if (viewModel.IsGroup.Value)
        {
            object? result = await SelectTypeOrReference();

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
        else
        {
            expandToggle.ContextFlyout?.ShowAt(expandToggle);
        }
    }

    private async Task<object?> SelectTypeOrReference()
    {
        if (_flyoutOpen) return null;

        try
        {
            _flyoutOpen = true;
            var selectVm = new SelectFilterEffectTypeViewModel();

            if (DataContext is FilterEffectEditorViewModel { IsDisposed: false } vm
                && PresenterTypeAttribute.GetPresenterType(typeof(FilterEffect)) != null)
            {
                var targets = vm.GetAvailableTargets();
                selectVm.InitializeReferences(targets);
            }

            return await LibraryItemPickerHelper.ShowAsync(this, selectVm, KnownLibraryItemFormats.FilterEffect);
        }
        finally
        {
            _flyoutOpen = false;
        }
    }

    private async void ChangeFilterTypeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FilterEffectEditorViewModel { IsDisposed: false } viewModel) return;

        object? result = await SelectTypeOrReference();

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
        if (_flyoutOpen) return;

        try
        {
            _flyoutOpen = true;
            var targets = vm.GetAvailableTargets();
            var pickerVm = new TargetPickerFlyoutViewModel();
            pickerVm.Initialize(targets);

            var flyout = new TargetPickerFlyout(pickerVm);
            flyout.ShowAt(this, true);

            var tcs = new TaskCompletionSource<FilterEffect?>();
            flyout.Dismissed += (_, _) => tcs.TrySetResult(null);
            flyout.Confirmed += (_, _) => tcs.TrySetResult(
                (pickerVm.SelectedItem.Value?.UserData as TargetObjectInfo)?.Object as FilterEffect);

            var result = await tcs.Task;
            if (result != null)
            {
                vm.SetTarget(result);
            }
        }
        finally
        {
            _flyoutOpen = false;
        }
    }
}
