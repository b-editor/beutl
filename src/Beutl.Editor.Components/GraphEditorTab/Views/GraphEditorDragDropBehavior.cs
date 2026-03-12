using Avalonia.Input;
using Avalonia.Xaml.Interactivity;
using Beutl.Animation.Easings;
using Beutl.Editor.Components.GraphEditorTab.ViewModels;
using Beutl.Editor.Components.Helpers;

namespace Beutl.Editor.Components.GraphEditorTab.Views;

public class GraphEditorDragDropBehavior : Behavior<GraphEditorView>
{
    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject == null) return;
        DragDrop.SetAllowDrop(AssociatedObject.graphPanel, true);
        AssociatedObject.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AssociatedObject.AddHandler(DragDrop.DropEvent, OnDrap);
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();
        if (AssociatedObject == null) return;
        DragDrop.SetAllowDrop(AssociatedObject.graphPanel, false);
        AssociatedObject.RemoveHandler(DragDrop.DragOverEvent, OnDragOver);
        AssociatedObject.RemoveHandler(DragDrop.DropEvent, OnDrap);
    }

    private void OnDrap(object? sender, DragEventArgs e)
    {
        if (AssociatedObject?.DataContext is not GraphEditorViewModel { Options.Value.Scale: var scale } viewModel)
            return;

        if (e.DataTransfer.TryGetValue(BeutlDataFormats.Easing) is not { } typeName) return;
        if (TypeFormat.ToType(typeName) is not { } type) return;
        if (Activator.CreateInstance(type) is not Easing easing) return;

        TimeSpan time = e.GetPosition(AssociatedObject.graphPanel).X.PixelToTimeSpan(scale);
        viewModel.DropEasing(easing, time);
        e.Handled = true;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(BeutlDataFormats.Easing))
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }
}
