using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Input;
using Avalonia.VisualTree;
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

public class KeyTimeBehavior : Behavior<Path>
{
    private GraphEditorView? _view;

    public static void SetAttached(Path obj, bool value)
    {
        if (value)
        {
            var behavior = new KeyTimeBehavior();
            Interaction.GetBehaviors(obj).Add(behavior);
        }
        else
        {
            var behaviors = Interaction.GetBehaviors(obj);
            var behavior = behaviors.OfType<KeyTimeBehavior>().FirstOrDefault();
            if (behavior != null)
            {
                behaviors.Remove(behavior);
            }
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

    // GraphEditorView, GraphEditorViewModel, GraphEditorKeyFrameViewModelを取得
    private bool TryGetValues(
        [NotNullWhen(true)] out GraphEditorView? view,
        [NotNullWhen(true)] out GraphEditorViewModel? viewModel,
        [NotNullWhen(true)] out GraphEditorKeyFrameViewModel? keyFrameViewModel)
    {
        view = _view ??= AssociatedObject.FindAncestorOfType<GraphEditorView>();
        viewModel = view?.DataContext as GraphEditorViewModel;
        keyFrameViewModel = AssociatedObject?.DataContext as GraphEditorKeyFrameViewModel;
        return view != null && viewModel != null && keyFrameViewModel != null;
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
