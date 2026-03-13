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
    public Point DragStart;
    // ViewControlPoint2は後ろの位置からの相対的な位置
    // ドラッグ前のコントロールポイントの位置（表示上の点）
    public (Point ControlPoint1, Point ControlPoint2)? ViewControlPoints;
    public (Point ControlPoint1, Point ControlPoint2)? NextViewControlPoints;

    public required IKeyFrame KeyFrame;
    public GraphEditorKeyFrameViewModel? KeyFrameViewModel;
    public GraphEditorKeyFrameViewModel? NextKeyFrameViewModel;

    public bool Crossed;

    // 追従移動するキーフレーム
    public GraphEditorKeyFrameViewModel[]? FollowingKeyFrames;
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
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();

        if (AssociatedObject == null) return;
        AssociatedObject.PointerPressed -= OnControlPointPointerPressed;
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

    private (Point, Point)? GetSplineControlPoints(GraphEditorKeyFrameViewModel keyFrame)
    {
        if (keyFrame.Model.Easing is SplineEasing)
        {
            var viewControlPoint1 = keyFrame.ControlPoint1.Value;
            var viewControlPoint2 = keyFrame.ControlPoint2.Value;
            viewControlPoint1 = keyFrame.LeftBottom.Value - viewControlPoint1;
            viewControlPoint2 = keyFrame.RightTop.Value - viewControlPoint2;

            return (viewControlPoint1, viewControlPoint2);
        }
        else
        {
            return default;
        }
    }

    private void OnControlPointPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!TryGetValues(out var view, out var editorViewModel, out var viewModel))
            return;

        PointerPoint point = e.GetCurrentPoint(view.grid);

        if (point.Properties.IsLeftButtonPressed)
        {
            int nextIndex = viewModel.Parent.KeyFrames.IndexOf(viewModel) + 1;
            view.KeyTimeMoveState = new KeyTimeMoveState
            {
                DragStart = point.Position,
                KeyFrame = viewModel.Model,
                KeyFrameViewModel = viewModel,
                ViewControlPoints = GetSplineControlPoints(viewModel),
                NextKeyFrameViewModel = viewModel.Parent.KeyFrames.ElementAtOrDefault(nextIndex),
                NextViewControlPoints = viewModel.Parent.KeyFrames.ElementAtOrDefault(nextIndex) is { } next
                    ? GetSplineControlPoints(next)
                    : null,
                Crossed = false,
                FollowingKeyFrames = e.KeyModifiers == KeyModifiers.Shift
                    ? viewModel.Parent.KeyFrames.Where(i => i != viewModel).ToArray()
                    : null
            };

            editorViewModel.BeginEditing();
            e.Handled = true;
        }
    }
}
