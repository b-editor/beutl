using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.Engine;
using Beutl.ViewModels.Dialogs;
using Beutl.ViewModels.Editors;
using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Editors;

public partial class CoreObjectListItemEditor : UserControl, IListItemEditor
{
    public CoreObjectListItemEditor()
    {
        InitializeComponent();
        ExpandTransitionHelper.Attach(reorderHandle, content, ExpandTransitionHelper.ListItemDuration);
        FallbackObjectViewHelper.Attach(this, view => content.Children.Add(view));

        reorderHandle.ContextFlyout = new FAMenuFlyout { Placement = PlacementMode.Pointer };
        EditorMenuHelper.AttachCopyPasteAndTemplateMenus(this, (FAMenuFlyout)reorderHandle.ContextFlyout);
    }

    public Control? ReorderHandle => reorderHandle;

    public event EventHandler? DeleteRequested;

    private void DeleteClick(object? sender, RoutedEventArgs e)
    {
        DeleteRequested?.Invoke(this, EventArgs.Empty);
    }

    private async void NewClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ICoreObjectEditorViewModel { IsDisposed: false } viewModel) return;

        await CoreObjectPickerHelper.NewInstanceOrPickAsync(this, viewModel);
    }

    private async void SelectTarget_Requested(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ICoreObjectEditorViewModel { IsDisposed: false } viewModel) return;

        await CoreObjectPickerHelper.PickTargetAsync(this, viewModel);
    }
}
