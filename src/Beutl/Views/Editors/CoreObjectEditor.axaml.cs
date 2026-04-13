using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Beutl.Editor.Components.ObjectPropertyTab.ViewModels;
using Beutl.Engine;
using Beutl.Services;
using Beutl.ViewModels;
using Beutl.ViewModels.Dialogs;
using Beutl.ViewModels.Editors;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Views.Editors;

public partial class CoreObjectEditor : UserControl
{
    private bool _flyoutOpen;

    public CoreObjectEditor()
    {
        InitializeComponent();
        ExpandTransitionHelper.Attach(expandToggle, content);
        FallbackObjectViewHelper.Attach(this, view => content.Children.Add(view));

        EditorMenuHelper.AttachCopyPasteAndTemplateMenus(
            this,
            (FAMenuFlyout)expandToggle.ContextFlyout!,
            (FAMenuFlyout)ReferenceMenuButton.Flyout!);

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, DragOver);
        AddHandler(DragDrop.DropEvent, Drop);
    }

    private void Drop(object? sender, DragEventArgs e)
    {
        if (DataContext is not BaseEditorViewModel { IsDisposed: false } viewModel) return;

        if (e.DataTransfer.TryGetFile()?.TryGetLocalPath() is { } droppedFile
            && string.Equals(Path.GetExtension(droppedFile), ".json", StringComparison.OrdinalIgnoreCase)
            && ObjectTemplateService.Instance.TryLoadFromFile(droppedFile) is { } template
            && viewModel.ApplyTemplate(template))
        {
            e.Handled = true;
        }
    }

    private void DragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(DataFormat.File))
        {
            e.DragEffects = DragDropEffects.Copy | DragDropEffects.Link;
            e.Handled = true;
        }
    }

    private void Navigate_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ICoreObjectEditorViewModel { IsDisposed: false } viewModel) return;
        if (viewModel.GetService<EditViewModel>() is not { } editViewModel) return;

        ObjectPropertyTabViewModel objViewModel
            = editViewModel.FindToolTab<ObjectPropertyTabViewModel>(i =>
                  ReferenceEquals(i.ChildContext.Value?.Target, viewModel.Value.Value))
              ?? new ObjectPropertyTabViewModel(editViewModel);

        objViewModel.NavigateCore(viewModel.Value.Value, false, viewModel);
        editViewModel.OpenToolTab(objViewModel);
    }

    private async void NewClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ICoreObjectEditorViewModel { IsDisposed: false } viewModel) return;

        Type propertyType = viewModel.PropertyAdapter.PropertyType;

        if (propertyType.IsSealed)
        {
            viewModel.SetNewInstance(propertyType);
            return;
        }

        object? result = await SelectTypeOrReference();

        switch (result)
        {
            case Type selectedType:
                viewModel.SetNewInstance(selectedType);
                break;
            case CoreObject target:
                viewModel.SetTarget(target);
                break;
        }
    }

    private void SetNullClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ICoreObjectEditorViewModel { IsDisposed: false } viewModel) return;

        viewModel.SetNull();
    }

    private void SelectTarget_Requested(object? sender, RoutedEventArgs e)
    {
        OnSelectTargetRequested();
    }

    private async Task<object?> SelectTypeOrReference()
    {
        if (_flyoutOpen) return null;
        if (DataContext is not ICoreObjectEditorViewModel { IsDisposed: false } viewModel) return null;

        try
        {
            _flyoutOpen = true;
            Type propertyType = viewModel.PropertyAdapter.PropertyType;
            string format = propertyType.FullName!;
            var selectVm = new SelectLibraryItemDialogViewModel(format, propertyType);

            if (PresenterTypeAttribute.GetPresenterType(propertyType) != null)
            {
                var targets = viewModel.GetAvailableTargets();
                selectVm.InitializeReferences(targets);
            }

            return await LibraryItemPickerHelper.ShowAsync(this, selectVm, format);
        }
        finally
        {
            _flyoutOpen = false;
        }
    }

    private async void OnSelectTargetRequested()
    {
        if (DataContext is not ICoreObjectEditorViewModel { IsDisposed: false } vm) return;
        if (_flyoutOpen) return;

        try
        {
            _flyoutOpen = true;
            var targets = vm.GetAvailableTargets();
            var pickerVm = new TargetPickerFlyoutViewModel();
            pickerVm.Initialize(targets);

            var flyout = new TargetPickerFlyout(pickerVm);
            flyout.ShowAt(this, true);

            var tcs = new TaskCompletionSource<CoreObject?>();
            flyout.Dismissed += (_, _) => tcs.TrySetResult(null);
            flyout.Confirmed += (_, _) => tcs.TrySetResult(
                (pickerVm.SelectedItem.Value?.UserData as TargetObjectInfo)?.Object);

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
