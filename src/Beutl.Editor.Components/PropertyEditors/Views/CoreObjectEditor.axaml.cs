using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Beutl.Editor.Components.ObjectPropertyTab.ViewModels;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Engine;
using Beutl.Services;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Editor.Components.PropertyEditors.Views;

public partial class CoreObjectEditor : UserControl
{
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
        if (DataContext is not IObjectEditorCommands { IsDisposed: false } viewModel) return;

        if (EditorDragDropHelper.TryApplyDroppedTemplate(e, viewModel))
        {
            e.Handled = true;
        }
    }

    private void DragOver(object? sender, DragEventArgs e)
    {
        EditorDragDropHelper.HandleTemplateFileDragOver(e);
    }

    private void Navigate_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ICoreObjectEditorViewModel { IsDisposed: false } viewModel) return;
        if (viewModel.GetService<IEditorContext>() is not { } editorContext) return;

        ObjectPropertyTabViewModel.Open(editorContext, viewModel.Value.Value, viewModel);
    }

    private async void NewClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ICoreObjectEditorViewModel { IsDisposed: false } viewModel) return;

        await CoreObjectPickerHelper.NewInstanceOrPickAsync(this, viewModel);
    }

    private void SetNullClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ICoreObjectEditorViewModel { IsDisposed: false } viewModel) return;

        viewModel.SetNull();
    }

    private async void SelectTarget_Requested(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ICoreObjectEditorViewModel { IsDisposed: false } viewModel) return;

        await CoreObjectPickerHelper.PickTargetAsync(this, viewModel);
    }
}
