using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Avalonia.Xaml.Interactivity;
using Beutl.Editor.Components.GraphEditorTab.ViewModels;

using Path = Avalonia.Controls.Shapes.Path;

namespace Beutl.Editor.Components.GraphEditorTab.Views;

public class ControlPointMoveState
{
    public Point DragStart;
    internal Action<bool>? Complete;
    internal GraphEditorKeyFrameViewModel Segment = null!;
    internal bool Incoming;
    internal Point HandleVector;
    internal Point OppositeVector;
}

public class ControlPointBehavior : Behavior<Path>
{
    private GraphEditorView? _view;

    public static void SetAttached(Path obj, bool value)
    {
        if (value)
        {
            var behavior = new ControlPointBehavior();
            Interaction.GetBehaviors(obj).Add(behavior);
        }
        else
        {
            var behaviors = Interaction.GetBehaviors(obj);
            var behavior = behaviors.OfType<ControlPointBehavior>().FirstOrDefault();
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
        AssociatedObject.PointerMoved += OnPointerMoved;
        AssociatedObject.PointerPressed += OnPointerPressed;
        AssociatedObject.PointerReleased += OnPointerReleased;
        AssociatedObject.ContextRequested += OnContextRequested;
        AssociatedObject.PointerCaptureLost += OnCaptureLost;
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();

        if (AssociatedObject == null) return;
        AssociatedObject.PointerMoved -= OnPointerMoved;
        AssociatedObject.PointerPressed -= OnPointerPressed;
        AssociatedObject.PointerReleased -= OnPointerReleased;
        AssociatedObject.ContextRequested -= OnContextRequested;
        AssociatedObject.PointerCaptureLost -= OnCaptureLost;
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

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!TryGetValues(out var view, out var editorViewModel, out _))
            return;

        if (view.ControlPointMoveState is not { } state)
            return;

        Point position = new(e.GetPosition(view.views).X, e.GetPosition(view.grid).Y);
        Point delta = position - state.DragStart;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) delta = delta.WithY(0);
        state.HandleVector += delta;
        state.DragStart = position;

        // Keep capture on the original visual. Only the edited side changes, so crossing
        // the key (and crossing back) remains one drag and one undo transaction.
        if (GraphEditorTangentCoupling.CrossedSegment(state.Segment, state.Incoming, state.HandleVector.X) is { } crossed)
        {
            UpdateHandle(state.Segment, state.Incoming, state.HandleVector.WithX(0));
            state.Segment = crossed;
            state.Incoming = !state.Incoming;
        }
        UpdateHandle(state.Segment, state.Incoming, state.HandleVector);

        if (!editorViewModel.Separately.Value && !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            var opposite = GraphEditorTangentCoupling.OppositeSegment(state.Segment, state.Incoming);
            if (opposite?.Model.Easing is Animation.Easings.SplineEasing)
            {
                Point vector = GraphEditorTangentCoupling.Opposite(editorViewModel,
                    GraphEditorTangentCoupling.Vector(state.Segment, state.Incoming), state.OppositeVector);
                UpdateHandle(opposite, !state.Incoming, GraphEditorTangentCoupling.ToPixels(editorViewModel, vector));
            }
        }

        e.Handled = true;
    }

    private static void UpdateHandle(GraphEditorKeyFrameViewModel segment, bool incoming, Point vector)
    {
        if (incoming) segment.UpdateControlPoint2(segment.RightTop.Value + vector);
        else segment.UpdateControlPoint1(segment.LeftBottom.Value + vector);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!TryGetValues(out var view, out var editorViewModel, out var viewModel))
            return;

        if (viewModel.Model.Easing is not Animation.Easings.SplineEasing)
            return;

        if (AssociatedObject == null)
            return;

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Alt)
            && view.GetVisualsAt(e.GetPosition(view)).OfType<Path>()
                .FirstOrDefault(path => path.Name == "KeyTimeIcon"
                    && path.DataContext is GraphEditorKeyFrameViewModel key && key.Parent == viewModel.Parent) is { } keyIcon)
        {
            keyIcon.RaiseEvent(e);
            return;
        }

        PointerPoint point = e.GetCurrentPoint(view.grid);

        if (point.Properties.IsLeftButtonPressed)
        {
            bool incoming = AssociatedObject.Tag is "ControlPoint2";
            view.ControlPointMoveState = new ControlPointMoveState
            {
                DragStart = new Point(e.GetPosition(view.views).X, point.Position.Y),
                Segment = viewModel,
                Incoming = incoming,
                HandleVector = incoming ? viewModel.ControlPoint2.Value - viewModel.RightTop.Value
                    : viewModel.ControlPoint1.Value - viewModel.LeftBottom.Value,
                OppositeVector = GraphEditorTangentCoupling.OppositeSegment(viewModel, incoming) is { } opposite
                    ? GraphEditorTangentCoupling.Vector(opposite, !incoming) : default
            };
            view.Focus();
            editorViewModel.HistoryManager.FlushPendingMutations();
            editorViewModel.HistoryManager.Commit();
            editorViewModel.BeginEditing();
            view.ControlPointMoveState.Complete = cancel =>
            {
                if (view.ControlPointMoveState == null) return;
                view.ControlPointMoveState = null;
                if (cancel) editorViewModel.HistoryManager.Rollback();
                editorViewModel.EndEditting();
                e.Pointer.Capture(null);
            };
            e.Pointer.Capture(AssociatedObject);
            e.Handled = true;
        }
    }

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (!TryGetValues(out var view, out _, out var viewModel)
            || !e.TryGetPosition(view, out Point position))
            return;

        // A handle can cover its own keyframe or the previous segment's keyframe.
        // Context requests are raised on release, separately from PointerPressed.
        Path? keyTimeIcon = view.GetVisualsAt(position)
            .OfType<Path>()
            .FirstOrDefault(path => path.Name == "KeyTimeIcon"
                && path.DataContext is GraphEditorKeyFrameViewModel keyFrame
                && keyFrame.Parent == viewModel.Parent);
        if (keyTimeIcon == null)
            return;

        var forwarded = new ContextRequestedEventArgs(e);
        keyTimeIcon.RaiseEvent(forwarded);
        e.Handled = forwarded.Handled;
    }

    private void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (TryGetValues(out var view, out _, out _))
            view.ControlPointMoveState?.Complete?.Invoke(true);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!TryGetValues(out var view, out var editorViewModel, out var viewModel))
            return;

        if (AssociatedObject?.Tag is not string tag)
            return;

        if (view.ControlPointMoveState != null)
        {
            view.ControlPointMoveState.Complete?.Invoke(false);
            e.Handled = true;
        }
    }
}
