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
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();

        if (AssociatedObject == null) return;
        AssociatedObject.PointerMoved -= OnPointerMoved;
        AssociatedObject.PointerPressed -= OnPointerPressed;
        AssociatedObject.PointerReleased -= OnPointerReleased;
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

    // 反対側のコントロールポイントのキーフレームを探す
    private static GraphEditorKeyFrameViewModel? FindOppositeKeyFrame(GraphEditorViewModel viewModel, GraphEditorKeyFrameViewModel item, string tag)
    {
        if (viewModel.SelectedView.Value is not { } selectedView) return null;
        int index = selectedView.KeyFrames.IndexOf(item);

        return tag switch
        {
            "ControlPoint1" => index == 0 ? null : selectedView.KeyFrames[index - 1],
            "ControlPoint2" => index == selectedView.KeyFrames.Count - 1 ? null : selectedView.KeyFrames[index + 1],
            _ => null
        };
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!TryGetValues(out var view, out var editorViewModel, out var viewModel))
            return;

        if (AssociatedObject?.Tag is not string tag)
            return;

        if (view.ControlPointMoveState == null)
            return;

        Point position = new(e.GetPosition(view.views).X, e.GetPosition(view.grid).Y);
        Point delta = position - view.ControlPointMoveState.DragStart;
        Point d = default;
        switch (tag)
        {
            case "ControlPoint1":
                viewModel.UpdateControlPoint1(viewModel.ControlPoint1.Value + delta);
                d = viewModel.LeftBottom.Value - viewModel.ControlPoint1.Value;
                break;
            case "ControlPoint2":
                viewModel.UpdateControlPoint2(viewModel.ControlPoint2.Value + delta);
                d = viewModel.RightTop.Value - viewModel.ControlPoint2.Value;
                break;
        }

        position = position.WithX(Math.Clamp(position.X, viewModel.Left.Value, viewModel.Right.Value));
        view.ControlPointMoveState.DragStart = position;

        if (!editorViewModel.Separately.Value)
        {
            double radians = Math.Atan2(d.X, d.Y);
            radians -= MathF.PI / 2;

            var oppotite = FindOppositeKeyFrame(editorViewModel, viewModel, tag);
            if (oppotite != null)
            {
                static double Length(Point p)
                {
                    return Math.Sqrt((p.X * p.X) + (p.Y * p.Y));
                }

                static Point CalculatePoint(double radians, double radius)
                {
                    double x = Math.Cos(radians) * radius;
                    double y = Math.Sin(radians) * radius;
                    // Y座標は反転
                    return new Point(x, -y);
                }

                bool symmetry = editorViewModel.Symmetry.Value;
                double length;
                switch (tag)
                {
                    case "ControlPoint2":
                        length = symmetry
                            ? Length(d)
                            : Length(oppotite.LeftBottom.Value - oppotite.ControlPoint1.Value);

                        oppotite.UpdateControlPoint1(oppotite.LeftBottom.Value + CalculatePoint(radians, length));
                        break;
                    case "ControlPoint1":
                        length = symmetry
                            ? Length(d)
                            : Length(oppotite.RightTop.Value - oppotite.ControlPoint2.Value);

                        oppotite.UpdateControlPoint2(oppotite.RightTop.Value + CalculatePoint(radians, length));
                        break;
                }
            }
        }

        e.Handled = true;
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
            && AssociatedObject.GetLogicalSiblings().OfType<Path>().FirstOrDefault(v => v.Name == "KeyTimeIcon") is Path ki
            && ki.InputHitTest(e.GetPosition(ki)) == ki)
        {
            ki.RaiseEvent(e);
            return;
        }

        PointerPoint point = e.GetCurrentPoint(view.grid);

        if (point.Properties.IsLeftButtonPressed)
        {
            view.ControlPointMoveState = new ControlPointMoveState
            {
                DragStart = new Point(e.GetPosition(view.views).X, point.Position.Y)
            };
            editorViewModel.BeginEditing();
            e.Handled = true;
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!TryGetValues(out var view, out var editorViewModel, out var viewModel))
            return;

        if (AssociatedObject?.Tag is not string tag)
            return;

        if (view.ControlPointMoveState != null)
        {
            editorViewModel.EndEditting();
            view.ControlPointMoveState = null;
            e.Handled = true;
        }
    }
}
