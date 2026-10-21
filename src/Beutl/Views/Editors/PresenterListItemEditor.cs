using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Engine;
using Beutl.ViewModels.Editors;
using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Editors;

// The list item editors of values that can be presenters. While the value is a presenter, the item is
// reordered by the presenter editor's handle, and the presenter editor picks the target.
public class PresenterListItemEditor : UserControl, IListItemEditor
{
    public static readonly DirectProperty<PresenterListItemEditor, Control?> ReorderHandleProperty =
        AvaloniaProperty.RegisterDirect<PresenterListItemEditor, Control?>(
            nameof(ReorderHandle),
            o => o.ReorderHandle);

    public event EventHandler? DeleteRequested;

    public Control? ReorderHandle
    {
        get;
        private set => SetAndRaise(ReorderHandleProperty, ref field, value);
    }

    // Called by the derived editors once InitializeComponent has created their parts.
    protected void AttachParts(ToggleButton reorderHandle, Panel content, PropertyEditor presenterEditor)
    {
        ExpandTransitionHelper.Attach(reorderHandle, content, ExpandTransitionHelper.ListItemDuration);
        FallbackObjectViewHelper.Attach(this, view => content.Children.Add(view));

        this.GetObservable(DataContextProperty)
            .Select(x => (x as IPresenterEditorViewModel)?.IsPresenter ?? Observable.Return(false))
            .Switch()
            .CombineLatest(presenterEditor.GetObservable(PropertyEditor.ReorderHandleProperty))
            .Subscribe(t => ReorderHandle = t.First ? t.Second : reorderHandle);

        reorderHandle.ContextFlyout = new FAMenuFlyout { Placement = PlacementMode.Pointer };
        EditorMenuHelper.AttachCopyPasteAndTemplateMenus(this, (FAMenuFlyout)reorderHandle.ContextFlyout);
    }

    protected void DeleteClick(object? sender, RoutedEventArgs e)
    {
        DeleteRequested?.Invoke(this, EventArgs.Empty);
    }

    protected async void SelectTarget_Requested(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not IPresenterEditorViewModel { IsDisposed: false } viewModel) return;

        await TargetSelectionHelper.HandleSelectTargetRequestAsync<IPresenterEditorViewModel, CoreObject>(
            this,
            viewModel,
            vm => vm.GetAvailableTargets(),
            (vm, target) => vm.SetTarget(target));
    }
}
