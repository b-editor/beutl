using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

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

    // The handle's value when the drag started, or the one the drag asks for.
    public double Value { get; } = value;
}

// Where the tonal wheels act, over a bar of luminance from black to white: shadows fade out up to Low and highlights
// fade in from High. A press on a handle grabs it and one on the bar brings the nearer handle there; the handles do
// not pass each other. The slider asks for values through its events and shows the Low and High it is given.
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

    internal double XOf(double value)
    {
        Rect bar = Bar;
        double ratio = Maximum > Minimum ? Math.Clamp((value - Minimum) / (Maximum - Minimum), 0, 1) : 0;
        return bar.Left + (ratio * bar.Width);
    }

    private double ValueAt(double x)
    {
        Rect bar = Bar;
        if (bar.Width <= 0)
            return Minimum;

        double ratio = Math.Clamp((x - bar.Left) / bar.Width, 0, 1);
        return Minimum + (ratio * (Maximum - Minimum));
    }

    protected override Size MeasureOverride(Size availableSize) => new(2 * Inset, 2 + BarHeight + GripSize + 2);

    public override void Render(DrawingContext context)
    {
        Rect bar = Bar;
        if (bar.Width <= 0)
            return;

        Pen? border = BorderBrush is { } borderBrush ? new Pen(borderBrush, 1) : null;
        context.DrawRectangle(s_luminance, border, new RoundedRect(bar, 3));
        DrawHandle(context, bar, XOf(Low));
        DrawHandle(context, bar, XOf(High));
    }

    private void DrawHandle(DrawingContext context, Rect bar, double x)
    {
        var outline = new Pen(HandleBorderBrush ?? Brushes.Black, 3);
        var line = new Pen(HandleBrush ?? Brushes.White, 1);
        var top = new Point(x, bar.Top - 1);
        var bottom = new Point(x, bar.Bottom + 1);
        context.DrawLine(outline, top, bottom);
        context.DrawLine(line, top, bottom);

        var grip = new StreamGeometry();
        using (StreamGeometryContext geometry = grip.Open())
        {
            geometry.BeginFigure(new Point(x, bar.Bottom + 1), true);
            geometry.LineTo(new Point(x + (GripSize / 2), bar.Bottom + GripSize + 1));
            geometry.LineTo(new Point(x - (GripSize / 2), bar.Bottom + GripSize + 1));
            geometry.EndFigure(true);
        }

        context.DrawGeometry(HandleBrush ?? Brushes.White, new Pen(HandleBorderBrush ?? Brushes.Black, 1), grip);
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

    // A handle stops at the other one, unless they were crossed to begin with.
    private void RaiseDelta(TonalRangeHandle handle, double value)
    {
        bool ordered = Low <= High;
        value = handle switch
        {
            TonalRangeHandle.Low when ordered => Math.Min(value, High),
            TonalRangeHandle.High when ordered => Math.Max(value, Low),
            _ => value,
        };
        RaiseEvent(new TonalRangeEventArgs(DragDeltaEvent, handle, value));
    }
}
