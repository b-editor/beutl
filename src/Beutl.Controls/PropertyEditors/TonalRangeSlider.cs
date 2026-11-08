using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Beutl.Controls.PropertyEditors;

public enum TonalRangeHandle
{
    Low,
    High,
}

public sealed class TonalRangeEventArgs(RoutedEvent routedEvent, TonalRangeHandle handle, double value)
    : RoutedEventArgs(routedEvent)
{
    public TonalRangeHandle Handle { get; } = handle;

    // The handle's value when the drag starts or ends, or the one the drag asks for.
    public double Value { get; } = value;
}

// Where the tonal wheels act, over a bar of luminance from black to white: shadows fade out up to Low and highlights
// fade in from High. A press on a handle grabs it and one on the bar brings the nearer handle there; the handles move
// in steps of Step and do not pass each other. The slider asks for values through its events and shows the Low and
// High it is given.
public class TonalRangeSlider : Control
{
    public static readonly StyledProperty<double> LowProperty =
        AvaloniaProperty.Register<TonalRangeSlider, double>(nameof(Low));

    public static readonly StyledProperty<double> HighProperty =
        AvaloniaProperty.Register<TonalRangeSlider, double>(nameof(High), 100);

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<TonalRangeSlider, double>(nameof(Minimum));

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<TonalRangeSlider, double>(nameof(Maximum), 100);

    // The step the handles move in, such as the ranges' SmallChange; zero moves them freely.
    public static readonly StyledProperty<double> StepProperty =
        AvaloniaProperty.Register<TonalRangeSlider, double>(nameof(Step));

    public static readonly StyledProperty<IBrush?> BorderBrushProperty =
        Border.BorderBrushProperty.AddOwner<TonalRangeSlider>();

    public static readonly StyledProperty<IBrush?> HandleBrushProperty =
        AvaloniaProperty.Register<TonalRangeSlider, IBrush?>(nameof(HandleBrush));

    public static readonly StyledProperty<IBrush?> HandleBorderBrushProperty =
        AvaloniaProperty.Register<TonalRangeSlider, IBrush?>(nameof(HandleBorderBrush));

    public static readonly RoutedEvent<TonalRangeEventArgs> DragStartedEvent =
        RoutedEvent.Register<TonalRangeSlider, TonalRangeEventArgs>(nameof(DragStarted), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<TonalRangeEventArgs> DragDeltaEvent =
        RoutedEvent.Register<TonalRangeSlider, TonalRangeEventArgs>(nameof(DragDelta), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<TonalRangeEventArgs> DragCompletedEvent =
        RoutedEvent.Register<TonalRangeSlider, TonalRangeEventArgs>(nameof(DragCompleted), RoutingStrategies.Bubble);

    internal const double Inset = 6;
    private const double BarHeight = 12;
    private const double GripSize = 8;
    private static readonly IBrush s_luminance = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Colors.Black, 0), new GradientStop(Colors.White, 1) },
    }.ToImmutable();

    private TonalRangeHandle? _dragging;
    private double _grabOffset;
    private StreamGeometry? _grip;

    static TonalRangeSlider()
    {
        AffectsRender<TonalRangeSlider>(
            LowProperty, HighProperty, MinimumProperty, MaximumProperty,
            BorderBrushProperty, HandleBrushProperty, HandleBorderBrushProperty);
        FocusableProperty.OverrideDefaultValue<TonalRangeSlider>(false);
    }

    public double Low
    {
        get => GetValue(LowProperty);
        set => SetValue(LowProperty, value);
    }

    public double High
    {
        get => GetValue(HighProperty);
        set => SetValue(HighProperty, value);
    }

    public double Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double Step
    {
        get => GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public IBrush? BorderBrush
    {
        get => GetValue(BorderBrushProperty);
        set => SetValue(BorderBrushProperty, value);
    }

    public IBrush? HandleBrush
    {
        get => GetValue(HandleBrushProperty);
        set => SetValue(HandleBrushProperty, value);
    }

    public IBrush? HandleBorderBrush
    {
        get => GetValue(HandleBorderBrushProperty);
        set => SetValue(HandleBorderBrushProperty, value);
    }

    public event EventHandler<TonalRangeEventArgs>? DragStarted
    {
        add => AddHandler(DragStartedEvent, value);
        remove => RemoveHandler(DragStartedEvent, value);
    }

    public event EventHandler<TonalRangeEventArgs>? DragDelta
    {
        add => AddHandler(DragDeltaEvent, value);
        remove => RemoveHandler(DragDeltaEvent, value);
    }

    public event EventHandler<TonalRangeEventArgs>? DragCompleted
    {
        add => AddHandler(DragCompletedEvent, value);
        remove => RemoveHandler(DragCompletedEvent, value);
    }

    private Rect Bar => new(Inset, 2, Math.Max(Bounds.Width - (2 * Inset), 0), BarHeight);

    internal double XOf(double value) => SliderTrack.XOf(value, Minimum, Maximum, Inset, Bar.Width);

    private double ValueAt(double x) => SliderTrack.ValueAt(x, Minimum, Maximum, Inset, Bar.Width);

    protected override Size MeasureOverride(Size availableSize) => new(2 * Inset, 2 + BarHeight + GripSize + 2);

    public override void Render(DrawingContext context)
    {
        Rect bar = Bar;
        if (bar.Width <= 0)
            return;

        // Immutable pens skip the compositor's per-frame resources, which a drag redraws every frame.
        IImmutableBrush fill = (HandleBrush ?? Brushes.White).ToImmutable();
        IImmutableBrush edge = (HandleBorderBrush ?? Brushes.Black).ToImmutable();
        var handle = new HandlePens(new ImmutablePen(edge, 3), new ImmutablePen(fill, 1), new ImmutablePen(edge, 1), fill);
        var border = BorderBrush is { } borderBrush ? new ImmutablePen(borderBrush.ToImmutable(), 1) : null;
        context.DrawRectangle(s_luminance, border, new RoundedRect(bar, 3));
        DrawHandle(context, bar, XOf(Low), handle);
        DrawHandle(context, bar, XOf(High), handle);
    }

    private void DrawHandle(DrawingContext context, Rect bar, double x, HandlePens pens)
    {
        var top = new Point(x, bar.Top - 1);
        var bottom = new Point(x, bar.Bottom + 1);
        context.DrawLine(pens.Outline, top, bottom);
        context.DrawLine(pens.Line, top, bottom);

        // The grip hangs under the bar at every handle alike, so one geometry at x = 0 serves both.
        _grip ??= CreateGrip(bar.Bottom + 1);
        using (context.PushTransform(Matrix.CreateTranslation(x, 0)))
        {
            context.DrawGeometry(pens.Fill, pens.GripEdge, _grip);
        }
    }

    private static StreamGeometry CreateGrip(double top)
    {
        var grip = new StreamGeometry();
        using StreamGeometryContext geometry = grip.Open();
        geometry.BeginFigure(new Point(0, top), true);
        geometry.LineTo(new Point(GripSize / 2, top + GripSize));
        geometry.LineTo(new Point(-GripSize / 2, top + GripSize));
        geometry.EndFigure(true);
        return grip;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        PointerPoint point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed || !IsEffectivelyEnabled)
            return;

        e.Handled = true;
        double x = point.Position.X;
        double lowX = XOf(Low);
        double highX = XOf(High);
        // On shared ground the press picks the side it lies on, so handles that meet can be pulled apart.
        TonalRangeHandle handle = Math.Abs(x - lowX) < Math.Abs(x - highX) || (Math.Abs(lowX - highX) < 0.5 && x < lowX)
            ? TonalRangeHandle.Low
            : TonalRangeHandle.High;
        double handleX = handle == TonalRangeHandle.Low ? lowX : highX;
        bool onHandle = Math.Abs(x - handleX) <= (GripSize / 2) + 2;
        _dragging = handle;
        _grabOffset = onHandle ? x - handleX : 0;
        e.Pointer.Capture(this);
        RaiseEvent(new TonalRangeEventArgs(DragStartedEvent, handle, ValueOf(handle)));
        if (!onHandle)
            RaiseDelta(handle, ValueAt(x));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging is not { } handle)
            return;

        e.Handled = true;
        RaiseDelta(handle, ValueAt(e.GetPosition(this).X - _grabOffset));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging.HasValue && e.InitialPressMouseButton == MouseButton.Left)
        {
            e.Handled = true;
            EndDrag();
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        EndDrag();
    }

    private void EndDrag()
    {
        if (_dragging is not { } handle)
            return;

        _dragging = null;
        RaiseEvent(new TonalRangeEventArgs(DragCompletedEvent, handle, ValueOf(handle)));
    }

    private double ValueOf(TonalRangeHandle handle) => handle == TonalRangeHandle.Low ? Low : High;

    // A handle moves in steps and stops at the other one, unless they were crossed to begin with. It stops again
    // after the rounding, which could carry it past another that lies between steps.
    private void RaiseDelta(TonalRangeHandle handle, double value)
    {
        double next = StopAtOther(handle, NumberEditorHelper.RoundToStep(StopAtOther(handle, value), Step));
        RaiseEvent(new TonalRangeEventArgs(DragDeltaEvent, handle, next));
    }

    private double StopAtOther(TonalRangeHandle handle, double value)
    {
        if (Low > High)
            return value;

        return handle == TonalRangeHandle.Low ? Math.Min(value, High) : Math.Max(value, Low);
    }

    private readonly record struct HandlePens(IPen Outline, IPen Line, IPen GripEdge, IImmutableBrush Fill);
}
