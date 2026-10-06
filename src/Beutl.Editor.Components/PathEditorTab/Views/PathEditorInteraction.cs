using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.VisualTree;
using Beutl.Composition;
using Beutl.Editor.Components.PathEditorTab.Services;
using Beutl.Editor.Components.Views;
using Beutl.Editor.Services;
using Microsoft.Extensions.DependencyInjection;
using Brushes = Avalonia.Media.Brushes;
using BtlPoint = Beutl.Graphics.Point;
using CubicBezierSegment = Beutl.Media.CubicBezierSegment;
using PathSegment = Beutl.Media.PathSegment;

namespace Beutl.Editor.Components.PathEditorTab.Views;

public enum PathEditorTool { Move, Pen, Bend, Hand }

// Shared by the dedicated path tab and the preview overlay.
internal sealed partial class PathEditorInteraction
{
    private readonly IPathEditorView _view;
    private readonly Control _host;
    private readonly Canvas _canvas;
    private readonly Action<Vector>? _pan;
    private readonly Action<double, Point>? _zoom;
    private readonly Action<bool>? _fit;
    private readonly Dictionary<PathEditorTool, RadioButton> _buttons = [];
    private readonly Border _marquee;
    private readonly Avalonia.Controls.Shapes.Ellipse _insertion;
    private readonly Avalonia.Controls.Shapes.Line _preview;
    private readonly Border _explicitStart;
    private Border? _floatingToolbar;
    private PathEditorTool _tool;
    private IPointer? _pointer;
    private Point _start;
    private Point _last;
    private bool _panning;
    private bool _space;
    private bool _adding;
    private HashSet<Thumb> _selectionBefore = [];
    private List<PathPointDragState>? _nudges;
    private List<PathPointDragState>? _coordinateStates;
    private IPathEditorContext? _coordinateContext;
    private BtlPoint _coordinateOrigin;
    private PathSegment? _penEnd;
    private BtlPoint? _outgoing;
    private CubicBezierSegment? _penSegment;
    private BtlPoint _penPoint;
    private CubicBezierSegment? _bending;
    private BtlPoint _bendControl1;
    private BtlPoint _bendControl2;
    private float _bendParameter;
    private BtlPoint? _firstIncoming;
    private const double HitRadius = 7;

    public PathEditorInteraction(IPathEditorView view, Canvas canvas,
        Action<Vector>? pan = null, Action<double, Point>? zoom = null, Action<bool>? fit = null, Panel? toolbarHost = null)
    {
        _view = view;
        _host = (Control)view;
        _canvas = canvas;
        _pan = pan;
        _zoom = zoom;
        _fit = fit;
        _host.Focusable = true;
        _marquee = new Border
        {
            IsHitTestVisible = false,
            IsVisible = false,
            BorderBrush = TimelineSharedObject.SelectionPen.Brush,
            BorderThickness = new Thickness(1),
            Background = TimelineSharedObject.SelectionFillBrush
        };
        _insertion = new Avalonia.Controls.Shapes.Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = Brushes.White,
            Stroke = Brushes.DodgerBlue,
            StrokeThickness = 1.5,
            IsHitTestVisible = false,
            IsVisible = false
        };
        _preview = new Avalonia.Controls.Shapes.Line
        {
            Stroke = Brushes.DodgerBlue,
            StrokeThickness = 1,
            StrokeDashArray = new Avalonia.Collections.AvaloniaList<double>([4, 3]),
            IsHitTestVisible = false,
            IsVisible = false
        };
        _explicitStart = new Border
        {
            Name = "PathStartPoint",
            Width = 8,
            Height = 8,
            CornerRadius = new Avalonia.CornerRadius(1),
            Background = Brushes.White,
            BorderBrush = Brushes.DodgerBlue,
            BorderThickness = new Thickness(1.5),
            IsHitTestVisible = false,
            IsVisible = false,
            ZIndex = 20
        };
        _host.GetResourceObservable("AccentFillColorDefaultBrush").Subscribe(value =>
        {
            if (value is IBrush brush)
            {
                _marquee.BorderBrush = _insertion.Stroke = _preview.Stroke = brush;
                _explicitStart.BorderBrush = brush;
                _marquee.Background = brush is ISolidColorBrush solid
                    ? new Avalonia.Media.SolidColorBrush(solid.Color, 0.12) : null;
            }
        });
        canvas.Children.Add(_preview);
        canvas.Children.Add(_marquee);
        canvas.Children.Add(_insertion);
        canvas.Children.Add(_explicitStart);
        AddToolbar(toolbarHost);
        canvas.AddHandler(InputElement.PointerPressedEvent, Pressed, RoutingStrategies.Tunnel);
        canvas.AddHandler(InputElement.PointerMovedEvent, Moved, RoutingStrategies.Tunnel);
        canvas.AddHandler(InputElement.PointerReleasedEvent, Released, RoutingStrategies.Tunnel);
        canvas.AddHandler(InputElement.PointerCaptureLostEvent, CaptureLost);
        _host.AddHandler(InputElement.KeyDownEvent, KeyDown);
        _host.AddHandler(InputElement.KeyUpEvent, KeyUp);
        _host.AddHandler(InputElement.LostFocusEvent, LostFocus);
        _host.GetObservable(Control.DataContextProperty).Subscribe(_ => Reset());
        _host.DetachedFromVisualTree += (_, _) => Reset();
    }

    private IPathEditorContext? Context => _view.DataContext as IPathEditorContext;
    private CompositionContext Composition => new(Context!.EditorContext.GetRequiredService<IEditorClock>().CurrentTime.Value);
    private IEnumerable<Thumb> Anchors => _canvas.Children.OfType<Thumb>().Where(t => !t.Classes.Contains("control"));

    public PathEditorTool Tool
    {
        get => _tool;
        set
        {
            _tool = value;
            _penEnd = null;
            _outgoing = _firstIncoming = null;
            _preview.IsVisible = _insertion.IsVisible = false;
            foreach (var (tool, button) in _buttons) button.IsChecked = tool == value;
            UpdateCursor();
            RefreshOverlays();
        }
    }

    public bool CanDragPoint(Thumb thumb) => !_space && _pointer == null
        && (Tool == PathEditorTool.Move || Tool == PathEditorTool.Bend && thumb.Classes.Contains("control"));

    private void AddToolbar(Panel? toolbarHost)
    {
        var panel = new StackPanel { Name = "PathTools", Orientation = Avalonia.Layout.Orientation.Horizontal };
        string group = $"PathTools-{Guid.NewGuid():N}";
        (PathEditorTool tool, string label, FluentIcons.Common.Icon icon)[] tools =
        [
            (PathEditorTool.Move, Strings.PathEditor_MoveTool, FluentIcons.Common.Icon.Cursor),
            (PathEditorTool.Pen, Strings.PathEditor_PenTool, FluentIcons.Common.Icon.Pen),
            (PathEditorTool.Bend, Strings.PathEditor_BendTool, FluentIcons.Common.Icon.BezierCurveSquare),
            (PathEditorTool.Hand, Strings.PathEditor_HandTool, FluentIcons.Common.Icon.HandLeft)
        ];
        foreach (var (tool, label, icon) in tools)
        {
            var button = new RadioButton
            {
                [!Control.ThemeProperty] = new DynamicResourceExtension("LiteNavRadioButton"),
                GroupName = group,
                Padding = new Thickness(7, 6, 5, 6),
                Content = new FluentIcons.Avalonia.Fluent.FluentIcon { Icon = icon, FontSize = 16 }
            };
            ToolTip.SetTip(button, label);
            AutomationProperties.SetName(button, label);
            button.Click += (_, _) => { Tool = tool; _host.Focus(); };
            _buttons.Add(tool, button);
            panel.Children.Add(button);
        }
        if (toolbarHost != null)
        {
            toolbarHost.Children.Add(panel);
        }
        else
        {
            // Preview editing uses the same controls without reserving any image space.
            panel.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            var bar = new Border
            {
                [!Border.CornerRadiusProperty] = new DynamicResourceExtension("OverlayCornerRadius"),
                Child = panel,
                Padding = new Thickness(4, 2),
                MinHeight = 38,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
                Margin = new Thickness(8),
                ZIndex = 100
            };
            bar.Bind(Border.BackgroundProperty, _host.GetResourceObservable("SolidBackgroundFillColorBaseBrush"));
            bar.Bind(Visual.IsVisibleProperty, _host.GetObservable(Visual.IsVisibleProperty));
            bar.Bind(InputElement.IsHitTestVisibleProperty, _host.GetObservable(InputElement.IsHitTestVisibleProperty));
            _floatingToolbar = bar;
            ((Panel)((UserControl)_host).Content!).Children.Add(bar);
        }
        Tool = PathEditorTool.Move;
    }

    public void SetToolbarHost(Panel host)
    {
        if (_floatingToolbar is not { } toolbar || ReferenceEquals(toolbar.Parent, host)) return;
        if (toolbar.Parent is Panel previous) previous.Children.Remove(toolbar);
        host.Children.Add(toolbar);
    }

    public void RefreshOverlays()
    {
        _explicitStart.IsVisible = false;
        if (Tool != PathEditorTool.Pen || _penEnd == null
            || Context?.PathFigure.Value is not { Segments.Count: > 0 } figure
            || !figure.Segments.Contains(_penEnd)) return;
        var context = Composition;
        BtlPoint start = figure.StartPoint.GetValue(context);
        if (start.IsInvalid || figure.IsClosed.GetValue(context) || !CanCloseExplicitPath(figure)) return;
        Point point = Screen(start);
        Canvas.SetLeft(_explicitStart, point.X - 4);
        Canvas.SetTop(_explicitStart, point.Y - 4);
        _explicitStart.IsVisible = true;
    }

    private static T? Ancestor<T>(object? source) where T : Visual =>
        source is T self ? self : (source as Visual)?.GetVisualAncestors().OfType<T>().FirstOrDefault();

    private void Pressed(object? sender, PointerPressedEventArgs e)
    {
        if (Ancestor<Button>(e.Source) != null) return;
        var properties = e.GetCurrentPoint(_canvas).Properties;
        if (!properties.IsLeftButtonPressed && !properties.IsMiddleButtonPressed) return;
        _host.Focus();
        Point point = e.GetPosition(_canvas);
        bool pan = properties.IsMiddleButtonPressed || _space || Tool == PathEditorTool.Hand;
        if (pan)
        {
            if (_pan == null) return;
            _panning = true;
            _last = e.GetPosition(TopLevel.GetTopLevel(_host));
            Capture(e);
            return;
        }
        if (Context?.PathFigure.Value is not { } figure) return;
        Thumb? thumb = Ancestor<Thumb>(e.Source);
        if ((Tool == PathEditorTool.Bend || e.ClickCount == 2) && thumb?.DataContext is PathSegment anchor && !thumb.Classes.Contains("control"))
        {
            Bend(figure, anchor);
            e.Handled = true;
            return;
        }
        if (Tool == PathEditorTool.Pen)
        {
            PressPen(e, figure, thumb, point);
            return;
        }
        if (Tool == PathEditorTool.Bend && HitEdge(point) is { } bend && TryLocal(point, out _penPoint))
        {
            BeginBendDrag(e, figure, bend);
            e.Handled = true;
            return;
        }
        if (thumb != null) return;
        if (e.ClickCount == 2 && HitEdge(point) is { } edge)
        {
            Insert(edge.index, edge.t);
            e.Handled = true;
            return;
        }
        BeginMarquee(e, point);
    }

    private void BeginMarquee(PointerPressedEventArgs e, Point point)
    {
        _start = point;
        _selectionBefore = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? _view.GetSelectedAnchors().ToHashSet() : [];
        foreach (Thumb item in Anchors) PathPointDragBehavior.SetIsSelected(item, _selectionBefore.Contains(item));
        SyncSelection();
        _marquee.Width = _marquee.Height = 0;
        _marquee.IsVisible = true;
        Capture(e);
    }

    private void Capture(PointerPressedEventArgs e)
    {
        _pointer = e.Pointer;
        e.Pointer.Capture(_canvas);
        e.PreventGestureRecognition();
        e.Handled = true;
    }

    private void Moved(object? sender, PointerEventArgs e)
    {
        Point position = e.GetPosition(_canvas);
        if (_penEnd != null && Context?.PathFigure.Value?.Segments.Contains(_penEnd) != true)
        {
            _penEnd = null;
            _outgoing = _firstIncoming = null;
            RefreshOverlays();
        }
        if (_pointer == null)
        {
            UpdateHoverOverlays(e, position);
            return;
        }
        if (_panning)
        {
            Point current = e.GetPosition(TopLevel.GetTopLevel(_host));
            _pan?.Invoke(current - _last);
            _last = current;
        }
        else if (_bending != null && TryLocal(position, out BtlPoint curvePoint))
        {
            var offset = (curvePoint - _penPoint) / (3 * _bendParameter * (1 - _bendParameter));
            _bending.ControlPoint1.CurrentValue = _bendControl1 + offset;
            _bending.ControlPoint2.CurrentValue = _bendControl2 + offset;
            _view.Refresh();
        }
        else if (_adding && TryLocal(position, out BtlPoint local))
        {
            if (((Vector)(position - _start)).Length > 3)
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) local = PathEditingOperations.SnapAngle(_penPoint, local);
                _outgoing = local;
                if (_penSegment != null)
                    _penSegment.ControlPoint2.CurrentValue = new(2 * _penPoint.X - local.X, 2 * _penPoint.Y - local.Y);
                else if (Context?.PathFigure.Value?.Segments.Count == 1)
                    _firstIncoming = new(2 * _penPoint.X - local.X, 2 * _penPoint.Y - local.Y);
                _view.Refresh();
            }
        }
        else if (_marquee.IsVisible)
        {
            UpdateMarquee(position);
        }
        e.Handled = true;
    }

    private void UpdateHoverOverlays(PointerEventArgs e, Point position)
    {
        var hit = Tool is PathEditorTool.Pen or PathEditorTool.Move && Ancestor<Thumb>(e.Source) == null
            && Ancestor<Button>(e.Source) == null ? HitEdge(position) : null;
        _insertion.IsVisible = hit is { } candidate && Context?.PathFigure.Value is { } hoveredFigure
            && PathEditingOperations.CanSplit(hoveredFigure, candidate.index);
        if (hit is { } h)
        {
            Canvas.SetLeft(_insertion, h.point.X - 4);
            Canvas.SetTop(_insertion, h.point.Y - 4);
        }
        _preview.IsVisible = Tool == PathEditorTool.Pen && _penEnd != null;
        if (_preview.IsVisible)
        {
            _preview.StartPoint = Screen(_penEnd!.GetEndPoint().GetValue(Composition));
            _preview.EndPoint = position;
        }
    }

    private void UpdateMarquee(Point position)
    {
        Rect rect = new Rect(_start, position).Normalize();
        Canvas.SetLeft(_marquee, rect.X);
        Canvas.SetTop(_marquee, rect.Y);
        _marquee.Width = rect.Width;
        _marquee.Height = rect.Height;
        foreach (Thumb thumb in Anchors)
            PathPointDragBehavior.SetIsSelected(thumb,
                _selectionBefore.Contains(thumb) || rect.Contains(PathEditorHelper.GetCanvasPosition(thumb)));
        SyncSelection();
    }

    private void Released(object? sender, PointerReleasedEventArgs e)
    {
        if (_pointer == null) return;
        FinishGesture();
        e.Handled = true;
    }

    private void CaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (ReferenceEquals(e.Source, _canvas)) FinishGesture();
    }

    private void FinishGesture()
    {
        var pointer = _pointer;
        _pointer = null;
        if (_adding || _bending != null)
        {
            Context?.EditorContext.GetRequiredService<HistoryManager>().Commit(CommandNames.EditPathPoint);
            _view.Refresh();
        }
        _bending = null;
        _adding = _panning = false;
        _marquee.IsVisible = false;
        if (pointer?.Captured == _canvas) pointer.Capture(null);
    }

    private void SyncSelection()
    {
        if (Context is { } context)
        {
            context.SelectedOperation.Value = _view.GetSelectedAnchors().LastOrDefault()?.DataContext as PathSegment;
            _view.Refresh();
        }
    }

    private void SelectOnly(PathSegment segment)
    {
        foreach (Thumb thumb in Anchors) PathPointDragBehavior.SetIsSelected(thumb, ReferenceEquals(thumb.DataContext, segment));
        if (Context is { } context) context.SelectedOperation.Value = segment;
        _view.Refresh();
    }

    private void UpdateCursor() => _canvas.Cursor = new Cursor(_space || Tool == PathEditorTool.Hand
        ? StandardCursorType.Hand : Tool is PathEditorTool.Pen or PathEditorTool.Bend ? StandardCursorType.Cross : StandardCursorType.Arrow);

    private void Reset()
    {
        FinishGesture();
        CommitNudge();
        CommitSelectionPosition();
        _space = false;
        Tool = PathEditorTool.Move;
    }

    private void Mutate(Action action)
    {
        Context!.EditorContext.GetRequiredService<HistoryManager>().ExecuteInTransaction(action, CommandNames.EditPathPoint);
        Context.FigureContext.Value?.InvalidateFrameCache();
        _view.Refresh();
    }
}
