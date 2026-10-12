using Avalonia;
using Avalonia.Input;
using Avalonia.Xaml.Interactivity;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Editor.Components.GraphEditorTab.ViewModels;

using Path = Avalonia.Controls.Shapes.Path;

namespace Beutl.Editor.Components.GraphEditorTab.Views;

public class KeyTimeMoveState
{
    internal GraphEditorDragSnapshot? Snapshot;
    internal Point Origin;
    internal Point ScrollDelta;
    internal bool? HorizontalConstraint;
    internal bool ToggleOnRelease;
    internal bool HasMoved;
    internal int TransformHandle = -1;
    internal Rect TransformBounds;
    internal Point TransformAnchor;

    public required IKeyFrame KeyFrame;
}

public class KeyTimeBehavior : GraphEditorPathBehavior
{
    public static void SetAttached(Path obj, bool value)
    {
        if (value)
        {
            var behavior = new KeyTimeBehavior();
            Interaction.GetBehaviors(obj).Add(behavior);
        }
    }

    protected override void OnAttached()
    {
        base.OnAttached();

        if (AssociatedObject == null) return;
        AssociatedObject.PointerPressed += OnControlPointPointerPressed;
        AssociatedObject.ContextRequested += OnContextRequested;
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();

        if (AssociatedObject == null) return;
        AssociatedObject.PointerPressed -= OnControlPointPointerPressed;
        AssociatedObject.ContextRequested -= OnContextRequested;
    }

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (TryGetValues(out _, out _, out var item) && !item.IsSelected.Value)
            item.Parent.SetSelection([item.Model]);
    }

    private void OnControlPointPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (TryGetValues(out var view, out _, out var keyFrame)
            && e.GetCurrentPoint(view).Properties.IsLeftButtonPressed)
        {
            view.StartKeyFrameDrag(keyFrame, e);
        }
    }
}
