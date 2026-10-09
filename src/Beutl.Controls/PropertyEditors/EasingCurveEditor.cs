using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Beutl.Animation.Easings;

namespace Beutl.Controls.PropertyEditors;

public sealed class EasingCurveEditedEventArgs(SplineEasing oldValue, SplineEasing newValue) : EventArgs
{
    public SplineEasing OldValue { get; } = oldValue;

    public SplineEasing NewValue { get; } = newValue;
}

// Plots an easing over progress [0, 1] and lets the control points of a spline be dragged.
// A drag never mutates the displayed spline: each step reports a new instance, so the owner
// can write it through the property and record the whole drag as one history entry.
public sealed class EasingCurveEditor : Control
{
    public static readonly StyledProperty<Easing?> EasingProperty =
        AvaloniaProperty.Register<EasingCurveEditor, Easing?>(nameof(Easing));

    public static readonly StyledProperty<bool> IsReadOnlyProperty =
        AvaloniaProperty.Register<EasingCurveEditor, bool>(nameof(IsReadOnly));

    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        AvaloniaProperty.Register<EasingCurveEditor, IBrush?>(nameof(Background));

    public static readonly StyledProperty<IBrush?> GridBrushProperty =
        AvaloniaProperty.Register<EasingCurveEditor, IBrush?>(nameof(GridBrush), Brushes.Gray);

    public static readonly StyledProperty<IBrush?> CurveBrushProperty =
        AvaloniaProperty.Register<EasingCurveEditor, IBrush?>(nameof(CurveBrush), Brushes.White);

    public static readonly StyledProperty<IBrush?> HandleBrushProperty =
        AvaloniaProperty.Register<EasingCurveEditor, IBrush?>(nameof(HandleBrush), Brushes.DodgerBlue);

    // Keeps handles placed on the plot corners fully inside the bounds.
    private const double Inset = 8;
    private const double HandleRadius = 4;
    private const double ActiveHandleRadius = 5.5;
    private const double HitRadius = 8;
    private const int SampleCount = 128;

    private ControlPoint _hovered;
    private ControlPoint _dragging;
    private SplineEasing? _dragStart;
    // The vertical range follows the curve, so it is held still while a handle is dragged
    // or the plot would rescale under the pointer.
    private ValueRange? _frozenRange;
    private SplineEasing? _observedSpline;
    private bool _isAttached;
    private Cursor? _handCursor;

    static EasingCurveEditor()
    {
        AffectsRender<EasingCurveEditor>(
            EasingProperty, IsReadOnlyProperty, BackgroundProperty, GridBrushProperty, CurveBrushProperty, HandleBrushProperty);
    }

    public EasingCurveEditor()
    {
        ClipToBounds = true;
    }

    public event EventHandler<EasingCurveEditedEventArgs>? Editing;

    public event EventHandler<EasingCurveEditedEventArgs>? Edited;

    private enum ControlPoint
    {
        None,
        First,
        Second
    }

    private readonly record struct ValueRange(double Minimum, double Maximum);

    public Easing? Easing
    {
        get => GetValue(EasingProperty);
        set => SetValue(EasingProperty, value);
    }

    public bool IsReadOnly
    {
        get => GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public IBrush? GridBrush
    {
        get => GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    public IBrush? CurveBrush
    {
        get => GetValue(CurveBrushProperty);
        set => SetValue(CurveBrushProperty, value);
    }

    public IBrush? HandleBrush
    {
        get => GetValue(HandleBrushProperty);
        set => SetValue(HandleBrushProperty, value);
    }

    public bool IsDragging => _dragging != ControlPoint.None;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == EasingProperty)
        {
            if (_isAttached)
            {
                ObserveSpline(change.GetNewValue<Easing?>() as SplineEasing);
            }

            if (IsDragging && change.GetNewValue<Easing?>() is not SplineEasing)
            {
                CancelDrag();
            }
        }
        else if (change.Property == IsReadOnlyProperty && change.GetNewValue<bool>())
        {
            // The steps so far were already written, so they are confirmed rather than left uncommitted.
            CompleteDrag();
            UpdateHover(ControlPoint.None);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        ObserveSpline(Easing as SplineEasing);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _isAttached = false;
        ObserveSpline(null);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsFinite(availableSize.Width) ? availableSize.Width : 160;
        double height = Math.Clamp(width * 0.6, 80, 140);
        if (double.IsFinite(availableSize.Height))
        {
            height = Math.Min(height, availableSize.Height);
        }

        return new Size(Math.Min(width, 160), height);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = new Rect(Bounds.Size);
        if (Background is { } background)
        {
            context.FillRectangle(background, bounds);
        }

        Easing? easing = Easing;
        ValueRange range = GetRange(easing);
        DrawGrid(context, range);

        if (easing is null) return;

        DrawCurve(context, easing, range);
        if (easing is SplineEasing spline)
        {
            DrawHandles(context, spline, range);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (IsReadOnly || IsDragging || Easing is not SplineEasing spline) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        ValueRange range = GetRange(spline);
        ControlPoint hit = HitTest(e.GetPosition(this), spline, range);
        if (hit == ControlPoint.None) return;

        _dragging = hit;
        _dragStart = spline;
        _frozenRange = range;
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!IsDragging)
        {
            UpdateHover(!IsReadOnly && Easing is SplineEasing hoverSpline
                ? HitTest(e.GetPosition(this), hoverSpline, GetRange(hoverSpline))
                : ControlPoint.None);
            return;
        }

        if (Easing is not SplineEasing current || _frozenRange is not { } range) return;

        (double x, double y) = FromScreen(e.GetPosition(this), range);
        // KeySpline rejects X outside [0, 1]; Y may overshoot to describe anticipation and overshoot.
        float newX = (float)Math.Clamp(x, 0, 1);
        float newY = (float)y;
        if (!float.IsFinite(newY)) return;

        SplineEasing next = _dragging == ControlPoint.First
            ? new SplineEasing(newX, newY, current.X2, current.Y2)
            : new SplineEasing(current.X1, current.Y1, newX, newY);
        e.Handled = true;
        Editing?.Invoke(this, new EasingCurveEditedEventArgs(current, next));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!IsDragging) return;

        CompleteDrag();
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        CompleteDrag();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (!IsDragging)
        {
            UpdateHover(ControlPoint.None);
        }
    }

    private void CompleteDrag()
    {
        if (!IsDragging) return;

        SplineEasing? start = _dragStart;
        EndDrag();
        if (start != null && Easing is SplineEasing end && !ReferenceEquals(start, end))
        {
            Edited?.Invoke(this, new EasingCurveEditedEventArgs(start, end));
        }
    }

    private void CancelDrag()
    {
        if (!IsDragging) return;

        EndDrag();
    }

    private void EndDrag()
    {
        _dragging = ControlPoint.None;
        _dragStart = null;
        _frozenRange = null;
        InvalidateVisual();
    }

    private void UpdateHover(ControlPoint hovered)
    {
        if (_hovered == hovered) return;

        _hovered = hovered;
        Cursor = hovered == ControlPoint.None ? null : _handCursor ??= new Cursor(StandardCursorType.Hand);
        InvalidateVisual();
    }

    private void ObserveSpline(SplineEasing? spline)
    {
        if (ReferenceEquals(_observedSpline, spline)) return;

        if (_observedSpline != null)
        {
            _observedSpline.Changed -= OnSplineChanged;
        }

        _observedSpline = spline;
        if (spline != null)
        {
            spline.Changed += OnSplineChanged;
        }
    }

    private void OnSplineChanged(object? sender, EventArgs e)
    {
        InvalidateVisual();
    }

    private ValueRange GetRange(Easing? easing)
    {
        if (_frozenRange is { } frozen) return frozen;

        double minimum = 0;
        double maximum = 1;
        if (easing != null)
        {
            for (int i = 0; i <= SampleCount; i++)
            {
                float value = easing.Ease(i / (float)SampleCount);
                if (float.IsFinite(value))
                {
                    minimum = Math.Min(minimum, value);
                    maximum = Math.Max(maximum, value);
                }
            }

            if (easing is SplineEasing spline)
            {
                minimum = Math.Min(minimum, Math.Min(spline.Y1, spline.Y2));
                maximum = Math.Max(maximum, Math.Max(spline.Y1, spline.Y2));
            }
        }

        return new ValueRange(minimum, maximum);
    }

    private Rect GetPlotRect()
    {
        var bounds = new Rect(Bounds.Size);
        return bounds.Width > Inset * 2 && bounds.Height > Inset * 2 ? bounds.Deflate(Inset) : bounds;
    }

    private Point ToScreen(double x, double y, ValueRange range)
    {
        Rect plot = GetPlotRect();
        return new Point(
            plot.X + x * plot.Width,
            plot.Bottom - (y - range.Minimum) / (range.Maximum - range.Minimum) * plot.Height);
    }

    private (double X, double Y) FromScreen(Point point, ValueRange range)
    {
        Rect plot = GetPlotRect();
        double x = plot.Width > 0 ? (point.X - plot.X) / plot.Width : 0;
        double y = plot.Height > 0
            ? range.Minimum + (plot.Bottom - point.Y) / plot.Height * (range.Maximum - range.Minimum)
            : 0;
        return (x, y);
    }

    private ControlPoint HitTest(Point position, SplineEasing spline, ValueRange range)
    {
        double first = Distance(position, ToScreen(spline.X1, spline.Y1, range));
        double second = Distance(position, ToScreen(spline.X2, spline.Y2, range));
        if (Math.Min(first, second) > HitRadius) return ControlPoint.None;

        return first <= second ? ControlPoint.First : ControlPoint.Second;
    }

    private static double Distance(Point a, Point b)
    {
        Vector delta = a - b;
        return Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
    }

    private void DrawGrid(DrawingContext context, ValueRange range)
    {
        if (GridBrush is not { } brush) return;

        var solid = new Pen(brush, 1);
        var dashed = new Pen(brush, 1, DashStyle.Dash);
        Point topLeft = ToScreen(0, 1, range);
        Point bottomRight = ToScreen(1, 0, range);
        context.DrawRectangle(null, solid, new Rect(topLeft, bottomRight));

        Point center = ToScreen(0.5, 0.5, range);
        context.DrawLine(dashed, new Point(center.X, topLeft.Y), new Point(center.X, bottomRight.Y));
        context.DrawLine(dashed, new Point(topLeft.X, center.Y), new Point(bottomRight.X, center.Y));
    }

    private void DrawCurve(DrawingContext context, Easing easing, ValueRange range)
    {
        var pen = new Pen(CurveBrush, 2) { LineJoin = PenLineJoin.Round, LineCap = PenLineCap.Round };
        var geometry = new StreamGeometry();
        using (StreamGeometryContext geometryContext = geometry.Open())
        {
            bool started = false;
            for (int i = 0; i <= SampleCount; i++)
            {
                float progress = i / (float)SampleCount;
                float value = easing.Ease(progress);
                if (!float.IsFinite(value))
                {
                    if (started)
                    {
                        geometryContext.EndFigure(false);
                        started = false;
                    }

                    continue;
                }

                Point point = ToScreen(progress, value, range);
                if (started)
                {
                    geometryContext.LineTo(point);
                }
                else
                {
                    geometryContext.BeginFigure(point, false);
                    started = true;
                }
            }

            if (started)
            {
                geometryContext.EndFigure(false);
            }
        }

        context.DrawGeometry(null, pen, geometry);
    }

    private void DrawHandles(DrawingContext context, SplineEasing spline, ValueRange range)
    {
        using DrawingContext.PushedState _ = context.PushOpacity(IsReadOnly ? 0.5 : 1);
        var linePen = new Pen(HandleBrush, 1);
        Point first = ToScreen(spline.X1, spline.Y1, range);
        Point second = ToScreen(spline.X2, spline.Y2, range);
        context.DrawLine(linePen, ToScreen(0, 0, range), first);
        context.DrawLine(linePen, ToScreen(1, 1, range), second);
        DrawHandle(context, first, ControlPoint.First);
        DrawHandle(context, second, ControlPoint.Second);
    }

    private void DrawHandle(DrawingContext context, Point center, ControlPoint point)
    {
        double radius = _dragging == point || (!IsDragging && _hovered == point) ? ActiveHandleRadius : HandleRadius;
        context.DrawEllipse(HandleBrush, null, center, radius, radius);
    }
}
