using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Xaml.Interactivity;
using Beutl.Animation;
using Beutl.Composition;
using Beutl.Controls;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.PathEditorTab.Services;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Media;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using BtlPoint = Beutl.Graphics.Point;
using BtlVector = Beutl.Graphics.Vector;

namespace Beutl.Editor.Components.PathEditorTab.Views;

public sealed class PathPointDragBehavior : Behavior<Thumb>
{
    public static readonly AttachedProperty<bool> IsSelectedProperty =
        AvaloniaProperty.RegisterAttached<PathPointDragBehavior, Thumb, bool>("IsSelected");

    private PathPointDragState? _dragState;
    private PathPointDragState[]? _coordDragStates;
    private Point? _lastPoint;
    private Point _startThumbPosition;
    private bool _toggleOnClick;
    private bool _collapseOnClick;
    private Control? _gestureHost;
    private IPointer? _gesturePointer;

    static PathPointDragBehavior()
    {
        IsSelectedProperty.Changed.Subscribe(e =>
            (e.Sender as Thumb)?.Classes.Set("selected", e.NewValue.GetValueOrDefault()));
    }

    public static void SetIsSelected(Thumb owner, bool value)
    {
        owner.SetValue(IsSelectedProperty, value);
    }

    public static bool GetIsSelected(Thumb owner)
    {
        return owner.GetValue(IsSelectedProperty);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject is { })
        {
            AssociatedObject.AddHandler(InputElement.PointerPressedEvent, OnThumbPointerPressed,
                handledEventsToo: true);
            AssociatedObject.AddHandler(InputElement.PointerReleasedEvent, OnThumbPointerReleased,
                handledEventsToo: true);
            AssociatedObject.AddHandler(InputElement.PointerMovedEvent, OnThumbPointerMoved, handledEventsToo: true);
            AssociatedObject.AddHandler(InputElement.PointerCaptureLostEvent, OnThumbPointerCaptureLost,
                handledEventsToo: true);
        }
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();
        if (AssociatedObject is { })
        {
            AssociatedObject.RemoveHandler(InputElement.PointerPressedEvent, OnThumbPointerPressed);
            AssociatedObject.RemoveHandler(InputElement.PointerReleasedEvent, OnThumbPointerReleased);
            AssociatedObject.RemoveHandler(InputElement.PointerMovedEvent, OnThumbPointerMoved);
            AssociatedObject.RemoveHandler(InputElement.PointerCaptureLostEvent, OnThumbPointerCaptureLost);
        }
    }

    private void OnReleased()
    {
        _gestureHost?.RemoveHandler(InputElement.KeyDownEvent, OnGestureKeyDown);
        _gestureHost = null;
        _gesturePointer = null;
        IPathEditorView? parent = AssociatedObject?.FindLogicalAncestorOfType<IPathEditorView>();
        if (parent is { DataContext: IPathEditorContext { Element.Value: { } element } viewModel })
        {
            parent.SkipUpdatePosition = false;
            if (_dragState != null)
                viewModel.EditorContext.GetRequiredService<HistoryManager>().Commit(CommandNames.EditPathPoint);
            parent.Refresh();
        }

        _coordDragStates = null;
        _dragState = null;
        _lastPoint = null;
    }

    private void OnThumbPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_lastPoint.HasValue)
        {
            e.Handled = true;

            OnReleased();
        }

        _coordDragStates = null;
        _dragState = null;
        _lastPoint = null;
    }

    private void OnThumbPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (AssociatedObject == null) return;

        if (e.InitialPressMouseButton == MouseButton.Right
            && AssociatedObject is { ContextFlyout: { } flyout })
        {
            flyout.ShowAt(AssociatedObject);
        }

        if (e.InitialPressMouseButton == MouseButton.Left && _lastPoint.HasValue)
        {
            e.Handled = true;

            if (_dragState == null && !AssociatedObject.Classes.Contains("control"))
            {
                if (_toggleOnClick)
                    SetIsSelected(AssociatedObject, false);
                else if (_collapseOnClick)
                {
                    var parent = AssociatedObject.FindLogicalAncestorOfType<IPathEditorView>();
                    foreach (Thumb item in parent?.GetSelectedAnchors() ?? [])
                        SetIsSelected(item, item == AssociatedObject);
                }
                SynchronizeSelection();
            }

            OnReleased();
        }

        _coordDragStates = null;
        _dragState = null;
        _lastPoint = null;
    }

    private static PathSegment? GetAnchor(IPathEditorContext viewModel, PathFigure figure, PathSegment segment,
        object? tag, IEnumerable<PathSegment>? selectedAnchors = null)
    {
        if (tag is not string name) return null;
        var property = PathEditorHelper.GetControlPointProperties(segment).FirstOrDefault(p => p.Name == name);
        if (property == null) return null;
        var clock = viewModel.EditorContext.GetRequiredService<IEditorClock>();
        var composition = new CompositionContext(clock.CurrentTime.Value);
        bool Connected(PathSegment anchor) => PathPointProperties.Get(figure, anchor, composition)
            .Any(p => p.Role != PathPointPropertyRole.Position && ReferenceEquals(p.Property, property));
        if (viewModel.SelectedOperation.Value is { } active && Connected(active)) return active;
        return (selectedAnchors ?? figure.Segments).FirstOrDefault(Connected);
    }

    private void OnThumbPointerMoved(object? sender, PointerEventArgs e)
    {
        IPathEditorView? parent = AssociatedObject?.FindLogicalAncestorOfType<IPathEditorView>();
        if (AssociatedObject is not { DataContext: PathSegment segment }
            || parent is not
            {
                DataContext: IPathEditorContext { PathFigure.Value: { } figure, Element.Value: { } element } viewModel
            }
            || !_lastPoint.HasValue)
        {
            return;
        }

        // Measure from a stable coordinate system; the thumb itself moves during the gesture.
        Point total = e.GetPosition((Control)parent) - _lastPoint.Value;
        if (_dragState == null && Math.Abs(total.X) < 3 && Math.Abs(total.Y) < 3) return;
        if ((_dragState == null || _coordDragStates == null)
            && !CreateDragState(parent, viewModel, AssociatedObject, figure, segment)) return;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && !AssociatedObject.Classes.Contains("control"))
            total = Math.Abs(total.X) >= Math.Abs(total.Y) ? new Point(total.X, 0) : new Point(0, total.Y);

        var linear = new Matrix(parent.Matrix.M11, parent.Matrix.M12,
            parent.Matrix.M21, parent.Matrix.M22, 0, 0);
        if (parent.Scale <= 0 || !linear.TryInvert(out Matrix inverse)) return;
        Point local = inverse.Transform(total / parent.Scale);
        var delta = new BtlVector((float)local.X, (float)local.Y);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && AssociatedObject.Classes.Contains("control")
            && GetAnchor(viewModel, figure, segment, AssociatedObject.Tag) is { } snapAnchor)
        {
            var clock = viewModel.EditorContext.GetRequiredService<IEditorClock>();
            BtlPoint origin = snapAnchor.GetEndPoint().GetValue(new CompositionContext(clock.CurrentTime.Value));
            Point initial = parent.Matrix.Invert().Transform(_startThumbPosition / parent.Scale);
            BtlPoint desired = new((float)initial.X + delta.X, (float)initial.Y + delta.Y);
            BtlPoint direction = desired - origin;
            float angle = MathF.Round(MathF.Atan2(direction.Y, direction.X) / (MathF.PI / 4)) * (MathF.PI / 4);
            float length = MathF.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
            delta = new BtlVector(origin.X + MathF.Cos(angle) * length - (float)initial.X,
                origin.Y + MathF.Sin(angle) * length - (float)initial.Y);
        }
        _dragState.MoveFromStart(delta);
        UpdatePosition(_dragState);

        void UpdatePosition(PathPointDragState state)
        {
            if (state.Thumb is not { } thumb) return;
            var clock = viewModel.EditorContext.GetRequiredService<IEditorClock>();
            Point point = state.GetInterpolatedValue(clock.CurrentTime.Value).ToAvaPoint();
            PathEditorHelper.SetCanvasPosition(thumb, parent.Matrix.Transform(point) * parent.Scale);
        }

        if (_coordDragStates != null)
        {
            if (AssociatedObject.Classes.Contains("control"))
            {
                if (!e.KeyModifiers.HasFlag(KeyModifiers.Alt)
                    && (viewModel.Symmetry.Value || viewModel.Asymmetry.Value))
                {
                    // ControlPointからAnchor(複数)を取得
                    // つながっているAnchorの反対側ごとに、角度、長さを計算

                    PathSegment? anchor = GetAnchor(viewModel, figure, segment, AssociatedObject.Tag);
                    if (anchor != null)
                    {
                        Debug.Assert(_coordDragStates.Length == 1 || _coordDragStates.Length == 0);

                        foreach (PathPointDragState c in _coordDragStates)
                        {
                            static float Length(BtlPoint p)
                            {
                                return MathF.Sqrt((p.X * p.X) + (p.Y * p.Y));
                            }

                            static BtlPoint CalculatePoint(float radians, float radius)
                            {
                                float x = MathF.Cos(radians) * radius;
                                float y = MathF.Sin(radians) * radius;
                                // Y座標は反転
                                return new(x, -y);
                            }

                            void UpdateThumbPosition(Thumb? thumb, BtlPoint point)
                            {
                                if (thumb == null) return;

                                Point p = parent.Matrix.Transform(point.ToAvaPoint());
                                p *= parent.Scale;
                                PathEditorHelper.SetCanvasPosition(thumb, p);
                            }

                            // アニメーションが有効な時は
                            // この区間の開始、終了キーフレームでのアンカーの位置を使う
                            if (c.Animation != null)
                            {
                                void Set(KeyFrame<BtlPoint>? keyframe)
                                {
                                    if (keyframe == null) return;

                                    TimeSpan keyTime = keyframe.KeyTime;

                                    if (!c.Animation.UseGlobalClock)
                                    {
                                        keyTime += element.Start;
                                    }

                                    var ctx = new CompositionContext(keyTime);
                                    BtlPoint anchorpoint = anchor.GetEndPoint().GetValue(ctx);
                                    BtlPoint point = _dragState.GetInterpolatedValue(keyTime);
                                    BtlPoint d = anchorpoint - point;
                                    float angle = MathF.Atan2(d.X, d.Y);
                                    angle -= MathF.PI / 2;

                                    float length;
                                    if (viewModel.Symmetry.Value)
                                    {
                                        length = Length(d);
                                    }
                                    else
                                    {
                                        BtlPoint d2 = anchorpoint - keyframe.Value;
                                        length = Length(d2);
                                    }

                                    keyframe.Value =
                                        PathEditorHelper.Round(anchorpoint + CalculatePoint(angle, length));
                                }

                                Set(c.Previous);
                                Set(c.Next);

                                var clock = viewModel.EditorContext.GetRequiredService<IEditorClock>();
                                UpdateThumbPosition(c.Thumb,
                                    c.GetInterpolatedValue(clock.CurrentTime.Value));
                            }
                            else
                            {
                                var clock = viewModel.EditorContext.GetRequiredService<IEditorClock>();
                                var ctx = new CompositionContext(clock.CurrentTime.Value);
                                BtlPoint point =
                                    _dragState.GetInterpolatedValue(clock.CurrentTime.Value);
                                BtlPoint anchorpoint = anchor.GetEndPoint().GetValue(ctx);
                                BtlPoint d = anchorpoint - point;
                                float angle = MathF.Atan2(d.X, d.Y);
                                angle -= MathF.PI / 2;

                                float length;
                                if (viewModel.Symmetry.Value)
                                {
                                    length = Length(d);
                                }
                                else
                                {
                                    BtlPoint d2 = anchorpoint - c.GetSampleValue(clock.CurrentTime.Value);
                                    length = Length(d2);
                                }

                                BtlPoint newValue = PathEditorHelper.Round(anchorpoint + CalculatePoint(angle, length));

                                c.SetValue(newValue);
                                UpdateThumbPosition(c.Thumb, newValue);
                            }
                        }
                    }
                }
            }
            else
            {
                foreach (PathPointDragState item in _coordDragStates)
                {
                    item.MoveFromStart(delta);
                    UpdatePosition(item);
                }
            }

            viewModel.FigureContext.Value?.InvalidateFrameCache();
        }
    }

    private void SetSelectedOperation(IPathEditorView view, IPathEditorContext viewModel, PathSegment segment)
    {
        if (AssociatedObject != null && viewModel.FigureContext.Value is { } figureContext)
        {
            figureContext.ExpandOperationForSegment(segment);

            if (!AssociatedObject.Classes.Contains("control"))
            {
                viewModel.SelectedOperation.Value = segment;
            }
            else if (viewModel.PathFigure.Value is { } figure
                && GetAnchor(viewModel, figure, segment, AssociatedObject.Tag,
                    view.GetSelectedAnchors().Select(t => t.DataContext).OfType<PathSegment>()) is { } anchor)
            {
                viewModel.SelectedOperation.Value = anchor;
            }
        }
    }

    private void OnThumbPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        IPathEditorView? parent = AssociatedObject?.FindLogicalAncestorOfType<IPathEditorView>();
        if (AssociatedObject is not { DataContext: PathSegment segment }
            || parent is not { DataContext: IPathEditorContext { PathFigure.Value: { } figure } viewModel })
        {
            return;
        }

        if (!e.GetCurrentPoint(AssociatedObject).Properties.IsLeftButtonPressed || !parent.CanDragPoint(AssociatedObject)) return;
        ((Control)parent).Focus();
        _gestureHost = (Control)parent;
        _gesturePointer = e.Pointer;
        _gestureHost.AddHandler(InputElement.KeyDownEvent, OnGestureKeyDown, RoutingStrategies.Tunnel);
        e.Handled = true;
        parent.SkipUpdatePosition = true;
        _lastPoint = e.GetPosition((Control)parent);
        _startThumbPosition = PathEditorHelper.GetCanvasPosition(AssociatedObject);
        e.Pointer.Capture(AssociatedObject);
        _toggleOnClick = _collapseOnClick = false;
        if (!AssociatedObject.Classes.Contains("control"))
        {
            bool selected = GetIsSelected(AssociatedObject);
            bool extend = (e.KeyModifiers & (KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Meta)) != 0;
            _toggleOnClick = selected && extend;
            _collapseOnClick = selected && !extend;
            if (!selected && !extend)
                foreach (Thumb item in parent.GetSelectedAnchors()) SetIsSelected(item, false);
            SetIsSelected(AssociatedObject, true);
        }
        SetSelectedOperation(parent, viewModel, segment);
    }

    private void OnGestureKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        var pointer = _gesturePointer;
        if (_dragState != null && _gestureHost?.DataContext is IPathEditorContext context)
            context.EditorContext.GetRequiredService<HistoryManager>().Rollback();
        _dragState = null;
        OnReleased();
        pointer?.Capture(null);
        e.Handled = true;
    }

    private void SynchronizeSelection()
    {
        if (AssociatedObject?.FindLogicalAncestorOfType<IPathEditorView>() is { } parent
            && parent.DataContext is IPathEditorContext context)
        {
            context.SelectedOperation.Value = parent.GetSelectedAnchors().LastOrDefault()?.DataContext as PathSegment;
            parent.Refresh();
        }
    }

    [MemberNotNullWhen(true, nameof(_dragState), nameof(_coordDragStates))]
    private bool CreateDragState(
        IPathEditorView view,
        IPathEditorContext viewModel,
        Thumb thumb,
        PathFigure figure,
        PathSegment segment)
    {
        IProperty<BtlPoint>? prop = PathEditorHelper.GetProperty(thumb);
        if (prop != null)
        {
            viewModel.EditorContext.GetRequiredService<HistoryManager>().Commit();
            _dragState = CreateThumbDragState(viewModel, segment, prop);
            _dragState.Thumb = thumb;

            if (!thumb.Classes.Contains("control"))
            {
                var list = new List<PathPointDragState>();
                CoordinateControlPoint(list, view, viewModel, figure, segment);
                foreach (Thumb anchor in view.GetSelectedAnchors())
                {
                    if (anchor == thumb) continue;

                    IProperty<BtlPoint>? prop2 = PathEditorHelper.GetProperty(anchor);
                    if (anchor.DataContext is PathSegment s && prop2 != null)
                    {
                        PathPointDragState d = CreateThumbDragState(viewModel, s, prop2);
                        d.Thumb = anchor;
                        list.Add(d);

                        CoordinateControlPoint(list, view, viewModel, figure, s);
                    }
                }

                _coordDragStates = list.DistinctBy(s => s.Property).Where(s => s.Property != prop).ToArray();
            }
            else
            {
                var list = new List<PathPointDragState>();
                CoordinateAnotherControlPoint(list, view, viewModel, figure, segment, prop);
                _coordDragStates = list.DistinctBy(s => s.Property).Where(s => s.Property != prop).ToArray();
            }

            return true;
        }
        else
        {
            return false;
        }
    }

    internal static void CoordinateControlPoint(
        List<PathPointDragState> list,
        IPathEditorView view,
        IPathEditorContext viewModel,
        PathFigure figure,
        PathSegment segment)
    {
        var clock = viewModel.EditorContext.GetRequiredService<IEditorClock>();
        foreach (var property in PathPointProperties.Get(figure, segment, new CompositionContext(clock.CurrentTime.Value)))
        {
            if (property.Role == PathPointPropertyRole.Position) continue;
            PathPointDragState state = CreateThumbDragState(viewModel, property.Owner, property.Property);
            state.Thumb = view.FindThumb(state.Target, state.Property);
            list.Add(state);
        }
    }

    internal static void CoordinateAnotherControlPoint(
        List<PathPointDragState> list,
        IPathEditorView view,
        IPathEditorContext viewModel,
        PathFigure figure,
        PathSegment segment,
        // [ControlPoint, ControlPoint1, ControlPoint2] のいずれか
        IProperty<BtlPoint> property)
    {
        if (GetAnchor(viewModel, figure, segment, property.Name) is not { } anchor) return;
        var clock = viewModel.EditorContext.GetRequiredService<IEditorClock>();
        foreach (var related in PathPointProperties.Get(figure, anchor, new CompositionContext(clock.CurrentTime.Value)))
        {
            if (related.Role == PathPointPropertyRole.Position || ReferenceEquals(related.Property, property)) continue;
            PathPointDragState state = CreateThumbDragState(viewModel, related.Owner, related.Property);
            state.Anchor = anchor;
            state.Thumb = view.FindThumb(state.Target, state.Property);
            list.Add(state);
        }
    }

    internal static bool IsClosed(IPathEditorContext context, PathFigure figure)
    {
        var clock = context.EditorContext.GetRequiredService<IEditorClock>();
        return figure.IsClosed.GetValue(new CompositionContext(clock.CurrentTime.Value));
    }

    internal static PathPointDragState CreateThumbDragState(
        IPathEditorContext viewModel,
        PathSegment segment,
        IProperty<BtlPoint> property)
    {
        var scene = viewModel.EditorContext.GetRequiredService<Scene>();
        var clock = viewModel.EditorContext.GetRequiredService<IEditorClock>();
        ProjectSystem.Element? element = viewModel.Element.Value;
        int rate = scene.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30;
        TimeSpan globalkeyTime = clock.CurrentTime.Value;
        TimeSpan localKeyTime = element != null ? globalkeyTime - element.Start : globalkeyTime;

        if (property.Animation is KeyFrameAnimation<BtlPoint> animation)
        {
            TimeSpan keyTime = animation.UseGlobalClock ? globalkeyTime : localKeyTime;
            keyTime = keyTime.RoundToRate(rate);

            (IKeyFrame? prev, IKeyFrame? next) = animation.KeyFrames.GetPreviousAndNextKeyFrame(keyTime);

            if (next?.KeyTime == keyTime)
                return new(property, segment, next as KeyFrame<BtlPoint>, null);

            return new(property, segment, prev as KeyFrame<BtlPoint>, next as KeyFrame<BtlPoint>);
        }

        return new(property, segment, null, null);
    }
}
