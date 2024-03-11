using System.Diagnostics.CodeAnalysis;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Xaml.Interactivity;

using Beutl.Animation;
using Beutl.Media;
using Beutl.ViewModels;
using Beutl.ViewModels.Editors;

using BtlPoint = Beutl.Graphics.Point;
using BtlVector = Beutl.Graphics.Vector;

namespace Beutl.Views;

public sealed class PathPointDragBehavior : Behavior<Thumb>
{
    public static readonly AttachedProperty<bool> IsSelectedProperty =
        AvaloniaProperty.RegisterAttached<PathPointDragBehavior, Thumb, bool>("IsSelected");

    private PathPointDragState? _dragState;
    private PathPointDragState[]? _coordDragStates;
    private Point? _lastPoint;

    static PathPointDragBehavior()
    {
        IsSelectedProperty.Changed.Subscribe(
            e => (e.Sender as Thumb)?.Classes.Set("selected", e.NewValue.GetValueOrDefault()));
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
            AssociatedObject.AddHandler(InputElement.PointerPressedEvent, OnThumbPointerPressed, handledEventsToo: true);
            AssociatedObject.AddHandler(InputElement.PointerReleasedEvent, OnThumbPointerReleased, handledEventsToo: true);
            AssociatedObject.AddHandler(InputElement.PointerMovedEvent, OnThumbPointerMoved, handledEventsToo: true);
            AssociatedObject.AddHandler(InputElement.PointerCaptureLostEvent, OnThumbPointerCaptureLost, handledEventsToo: true);
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
        IPathEditorView? parent = AssociatedObject?.FindLogicalAncestorOfType<IPathEditorView>();
        if (parent is { DataContext: IPathEditorViewModel { Element.Value: { } element } viewModel })
        {
            parent.SkipUpdatePosition = false;
            IRecordableCommand? command = _dragState?.CreateCommand([]);
            if (_coordDragStates?.Length > 0)
            {
                command = _coordDragStates.Aggregate(command, (a, b) => a.Append(b.CreateCommand([])));
            }

            if (command != null)
            {
                command = command.WithStoables([element]);

                command.DoAndRecord(viewModel.EditViewModel.CommandRecorder);
            }
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

            if (!AssociatedObject.Classes.Contains("control"))
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
                {
                    SetIsSelected(AssociatedObject, !GetIsSelected(AssociatedObject));
                }
                else if (_dragState == null || _coordDragStates == null)
                {
                    IPathEditorView? parent = AssociatedObject?.FindLogicalAncestorOfType<IPathEditorView>();
                    if (parent != null)
                    {
                        foreach (Thumb item in parent.GetSelectedAnchors())
                        {
                            SetIsSelected(item, false);
                        }
                    }
                }
            }

            OnReleased();
        }

        _coordDragStates = null;
        _dragState = null;
        _lastPoint = null;
    }

    private static PathSegment? GetAnchor(IPathEditorViewModel viewModel, PathFigure figure, PathSegment segment, object? tag)
    {
        if (tag is not string s || figure.Segments.Count <= 1) return null;

        int index = figure.Segments.IndexOf(segment);
        int previndex = (index - 1 + figure.Segments.Count) % figure.Segments.Count;
        if (s == "ControlPoint1")
        {
            return figure.Segments[previndex];
        }
        else if (s == "ControlPoint2")
        {
            return segment;
        }
        else if (s == "ControlPoint")
        {
            PathSegment? selected = viewModel.SelectedOperation.Value;
            if (selected != segment)
            {
                return figure.Segments[previndex];
            }
            else
            {
                return segment;
            }
        }
        else
        {
            return null;
        }
    }

    private void OnThumbPointerMoved(object? sender, PointerEventArgs e)
    {
        IPathEditorView? parent = AssociatedObject?.FindLogicalAncestorOfType<IPathEditorView>();
        if (AssociatedObject is not { DataContext: PathSegment segment }
            || parent is not { DataContext: IPathEditorViewModel { PathFigure.Value: { } figure, Element.Value: { } element } viewModel }
            || !_lastPoint.HasValue)
        {
            return;
        }

        // _dragState, _coordDragStatesがnullの場合、作成。
        if ((_dragState == null || _coordDragStates == null)
            && !CreateDragState(parent, viewModel, AssociatedObject, figure, segment))
        {
            return;
        }

        Point vector = e.GetPosition(AssociatedObject) - _lastPoint.Value;

        var delta = new BtlVector((float)(vector.X / parent.Scale), (float)(vector.Y / parent.Scale));
        Graphics.Matrix mat = new Graphics.Matrix(
            (float)parent.Matrix.M11, (float)parent.Matrix.M12,
            (float)parent.Matrix.M21, (float)parent.Matrix.M22,
            0, 0).Invert();
        delta = mat.Transform((BtlPoint)delta);

        _dragState.Move(delta);
        if (_dragState.Thumb is { } thumb)
        {
            var p = PathEditorHelper.Round(
                PathEditorHelper.GetCanvasPosition(thumb) + vector,
                parent.Matrix);

            PathEditorHelper.SetCanvasPosition(thumb, p);
        }

        if (_coordDragStates != null)
        {
            if (AssociatedObject.Classes.Contains("control"))
            {
                if (viewModel.Symmetry.Value || viewModel.Asymmetry.Value)
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

                                    TimeSpan localkeyTime = keyframe.KeyTime;
                                    TimeSpan keyTime = keyframe.KeyTime;

                                    if (c.Animation.UseGlobalClock)
                                    {
                                        localkeyTime -= element.Start;
                                    }
                                    else
                                    {
                                        keyTime += element.Start;
                                    }

                                    BtlPoint anchorpoint = anchor.GetEndPoint(localkeyTime, keyTime);
                                    BtlPoint point = _dragState.GetInterpolatedValue(element, keyTime);
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

                                    keyframe.Value = PathEditorHelper.Round(anchorpoint + CalculatePoint(angle, length));
                                }

                                Set(c.Previous);
                                Set(c.Next);

                                UpdateThumbPosition(c.Thumb, c.GetInterpolatedValue(element, viewModel.EditViewModel.CurrentTime.Value));
                            }
                            else
                            {
                                BtlPoint point = _dragState.GetInterpolatedValue(element, viewModel.EditViewModel.CurrentTime.Value);
                                BtlPoint anchorpoint = anchor.GetEndPoint();
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
                                    BtlPoint d2 = anchorpoint - c.GetSampleValue();
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
                    item.Move(delta);
                    if (item.Thumb is { } thumb1)
                    {
                        var p = PathEditorHelper.Round(
                            PathEditorHelper.GetCanvasPosition(thumb1) + vector,
                            parent.Matrix);

                        PathEditorHelper.SetCanvasPosition(thumb1, p);
                    }
                }
            }
        }
    }

    private void SetSelectedOperation(IPathEditorViewModel viewModel, PathSegment segment)
    {
        if (AssociatedObject != null
            && viewModel is { FigureContext.Value.Group.Value: { } group })
        {
            foreach (ListItemEditorViewModel<PathSegment> item in group.Items)
            {
                if (item.Context is PathOperationEditorViewModel itemvm)
                {
                    if (ReferenceEquals(itemvm.Value.Value, segment))
                    {
                        itemvm.IsExpanded.Value = true;
                        itemvm.ProgrammaticallyExpanded = true;
                    }
                    else if (itemvm.ProgrammaticallyExpanded)
                    {
                        itemvm.IsExpanded.Value = false;
                    }
                }
            }

            if (!AssociatedObject.Classes.Contains("control"))
            {
                viewModel.SelectedOperation.Value = segment;
            }
        }
    }

    private void OnThumbPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        IPathEditorView? parent = AssociatedObject?.FindLogicalAncestorOfType<IPathEditorView>();
        if (AssociatedObject is not { DataContext: PathSegment segment }
            || parent is not { DataContext: IPathEditorViewModel { PathFigure.Value: { } figure } viewModel })
        {
            return;
        }

        e.Handled = true;
        parent.SkipUpdatePosition = true;
        _lastPoint = e.GetPosition(AssociatedObject);

        SetSelectedOperation(viewModel, segment);

        //CreateDragState(parent, viewModel, AssociatedObject, figure, segment);
    }

    [MemberNotNullWhen(true, nameof(_dragState), nameof(_coordDragStates))]
    private bool CreateDragState(
        IPathEditorView view,
        IPathEditorViewModel viewModel,
        Thumb thumb,
        PathFigure figure,
        PathSegment segment)
    {
        CoreProperty<BtlPoint>? prop = PathEditorHelper.GetProperty(thumb);
        if (prop != null)
        {
            _dragState = CreateThumbDragState(viewModel, segment, prop);
            _dragState.Thumb = thumb;

            if (!thumb.Classes.Contains("control"))
            {
                var list = new List<PathPointDragState>();
                CoordinateControlPoint(list, view, viewModel, figure, segment);
                foreach (Thumb anchor in view.GetSelectedAnchors())
                {
                    if (anchor == thumb) continue;

                    CoreProperty<BtlPoint>? prop2 = PathEditorHelper.GetProperty(anchor);
                    if (anchor.DataContext is PathSegment s && prop2 != null)
                    {
                        PathPointDragState d = CreateThumbDragState(viewModel, s, prop2);
                        d.Thumb = anchor;
                        list.Add(d);

                        CoordinateControlPoint(list, view, viewModel, figure, s);
                    }
                }

                _coordDragStates = [.. list];
            }
            else
            {
                var list = new List<PathPointDragState>();
                CoordinateAnotherControlPoint(list, view, viewModel, figure, segment, prop);
                _coordDragStates = [.. list];
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
        IPathEditorViewModel viewModel,
        PathFigure figure,
        PathSegment segment)
    {
        CoreProperty<BtlPoint>[] props = PathEditorHelper.GetControlPointProperties(segment);
        if (props.Length > 0)
        {
            PathPointDragState state = CreateThumbDragState(viewModel, segment, props[^1]);
            state.Thumb = view.FindThumb(state.Target, state.Property);
            list.Add(state);
        }

        int index = figure.Segments.IndexOf(segment);
        int nextIndex = (index + 1) % figure.Segments.Count;

        if (0 <= nextIndex && nextIndex < figure.Segments.Count)
        {
            PathSegment nextSegment = figure.Segments[nextIndex];
            props = PathEditorHelper.GetControlPointProperties(nextSegment);
            if (props.Length > 0)
            {
                PathPointDragState state = CreateThumbDragState(viewModel, nextSegment, props[0]);
                state.Thumb = view.FindThumb(state.Target, state.Property);
                list.Add(state);
            }
        }
    }

    internal static void CoordinateAnotherControlPoint(
        List<PathPointDragState> list,
        IPathEditorView view,
        IPathEditorViewModel viewModel,
        PathFigure figure,
        PathSegment segment,
        // [ControlPoint, ControlPoint1, ControlPoint2] のいずれか
        CoreProperty<BtlPoint> property)
    {
        int index = figure.Segments.IndexOf(segment);
        if (index < 0 || figure.Segments.Count == 0) return;

        if (segment is CubicBezierSegment)
        {
            PathSegment? asegment = null;
            PathSegment? anchor = null;
            int apropIndex = -1;

            if (property == CubicBezierSegment.ControlPoint1Property)
            {
                int aindex = (index - 1 + figure.Segments.Count) % figure.Segments.Count;
                asegment = figure.Segments[aindex];
                apropIndex = 1;
                anchor = asegment;
            }
            else if (property == CubicBezierSegment.ControlPoint2Property)
            {
                int aindex = (index + 1) % figure.Segments.Count;
                asegment = figure.Segments[aindex];
                apropIndex = 0;
                anchor = segment;
            }

            if (asegment != null)
            {
                CoreProperty<BtlPoint>? aproperty = PathEditorHelper.GetControlPointProperty(asegment, apropIndex);
                if (aproperty != null)
                {
                    PathPointDragState state = CreateThumbDragState(viewModel, asegment, aproperty);
                    state.Anchor = anchor;
                    state.Thumb = view.FindThumb(state.Target, state.Property);
                    list.Add(state);
                }
            }
        }
        else if (segment is QuadraticBezierSegment or ConicSegment)
        {
            void Add(int aindex, int apropIndex, PathSegment? anchor)
            {
                PathSegment asegment = figure.Segments[aindex];
                anchor ??= asegment;

                CoreProperty<BtlPoint>? aproperty = PathEditorHelper.GetControlPointProperty(asegment, apropIndex);
                if (aproperty != null)
                {
                    PathPointDragState state = CreateThumbDragState(viewModel, asegment, aproperty);
                    state.Anchor = anchor;
                    state.Thumb = view.FindThumb(state.Target, state.Property);
                    list.Add(state);
                }
            }

            PathSegment? selected = viewModel.SelectedOperation.Value;
            if (selected != segment)
            {
                Add((index - 1 + figure.Segments.Count) % figure.Segments.Count, 1, segment);
            }
            else
            {
                Add((index + 1) % figure.Segments.Count, 0, null);
            }
        }
    }

    internal static PathPointDragState CreateThumbDragState(
        IPathEditorViewModel viewModel,
        PathSegment segment,
        CoreProperty<BtlPoint> property)
    {
        EditViewModel editViewModel = viewModel.EditViewModel;
        ProjectSystem.Element? element = viewModel.Element.Value;
        int rate = editViewModel.Scene.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30;
        TimeSpan globalkeyTime = editViewModel.CurrentTime.Value;
        TimeSpan localKeyTime = element != null ? globalkeyTime - element.Start : globalkeyTime;

        if (segment.Animations.FirstOrDefault(v => v.Property == property) is KeyFrameAnimation<BtlPoint> animation)
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
