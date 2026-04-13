using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.Controls.PropertyEditors;
using Beutl.Graphics.Effects;
using Beutl.ViewModels.Editors;
using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Editors;

public partial class FilterEffectListItemEditor : UserControl, IListItemEditor
{
    public static readonly DirectProperty<FilterEffectListItemEditor, Control?> ReorderHandleProperty =
        AvaloniaProperty.RegisterDirect<FilterEffectListItemEditor, Control?>(
            nameof(ReorderHandle),
            o => o.ReorderHandle);

    public FilterEffectListItemEditor()
    {
        InitializeComponent();
        ExpandTransitionHelper.Attach(reorderHandle, content, ExpandTransitionHelper.ListItemDuration);
        FallbackObjectViewHelper.Attach(this, view => content.Children.Add(view));

        this.GetObservable(DataContextProperty)
            .Select(x => x as FilterEffectEditorViewModel)
            .Select(x => x?.IsPresenter ?? Observable.Return(false))
            .Switch()
            .CombineLatest(presenterEditor.GetObservable(PropertyEditor.ReorderHandleProperty))
            .Subscribe(t => UpdateReorderHandle(t.First, t.Second));

        reorderHandle.ContextFlyout = new FAMenuFlyout { Placement = PlacementMode.Pointer };
        EditorMenuHelper.AttachCopyPasteAndTemplateMenus(this, (FAMenuFlyout)reorderHandle.ContextFlyout);
    }

    public Control? ReorderHandle
    {
        get;
        private set => SetAndRaise(ReorderHandleProperty, ref field, value);
    }

    private void UpdateReorderHandle(bool isPresenter, Control? presenterReorderHandle)
    {
        ReorderHandle = isPresenter
            ? presenterReorderHandle
            : reorderHandle;
    }

    public event EventHandler? DeleteRequested;

    private void DeleteClick(object? sender, RoutedEventArgs e)
    {
        DeleteRequested?.Invoke(this, EventArgs.Empty);
    }

    private async void SelectTarget_Requested(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FilterEffectEditorViewModel { IsDisposed: false } vm) return;

        await TargetSelectionHelper.HandleSelectTargetRequestAsync<FilterEffectEditorViewModel, FilterEffect>(
            this,
            vm,
            vm => vm.GetAvailableTargets(),
            (vm, target) => vm.SetTarget(target));
    }
}
