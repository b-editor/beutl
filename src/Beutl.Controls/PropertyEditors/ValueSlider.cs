using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Beutl.Controls.PropertyEditors;

// A slim slider drawn in one piece: a rail, the part between Origin and Value filled, and a round thumb. It takes no
// focus or keys, and leaves its pointer to the editor that owns it, as NumberSliderEditor does.
public class ValueSlider : Control
{
    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<ValueSlider, double>(nameof(Minimum));

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<ValueSlider, double>(nameof(Maximum), 1);

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<ValueSlider, double>(nameof(Value));

    // Where the fill starts, such as the property's default.
    public static readonly StyledProperty<double> OriginProperty =
        AvaloniaProperty.Register<ValueSlider, double>(nameof(Origin));

    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        Border.BackgroundProperty.AddOwner<ValueSlider>();

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<ValueSlider, IBrush?>(nameof(Foreground));

    public static readonly StyledProperty<IBrush?> ThumbBrushProperty =
        AvaloniaProperty.Register<ValueSlider, IBrush?>(nameof(ThumbBrush));

    public static readonly StyledProperty<IBrush?> ThumbBorderBrushProperty =
        AvaloniaProperty.Register<ValueSlider, IBrush?>(nameof(ThumbBorderBrush));

    // The mark at the origin.
    public static readonly StyledProperty<IBrush?> TickBrushProperty =
        AvaloniaProperty.Register<ValueSlider, IBrush?>(nameof(TickBrush));

    internal const double ThumbRadius = 6;
    private const double RailThickness = 4;

    static ValueSlider()
    {
        AffectsRender<ValueSlider>(
            MinimumProperty, MaximumProperty, ValueProperty, OriginProperty,
            BackgroundProperty, ForegroundProperty, ThumbBrushProperty, ThumbBorderBrushProperty, TickBrushProperty);
        FocusableProperty.OverrideDefaultValue<ValueSlider>(false);
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

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Origin
    {
        get => GetValue(OriginProperty);
        set => SetValue(OriginProperty, value);
    }

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public IBrush? ThumbBrush
    {
        get => GetValue(ThumbBrushProperty);
        set => SetValue(ThumbBrushProperty, value);
    }

    public IBrush? ThumbBorderBrush
    {
        get => GetValue(ThumbBorderBrushProperty);
        set => SetValue(ThumbBorderBrushProperty, value);
    }

    public IBrush? TickBrush
    {
        get => GetValue(TickBrushProperty);
        set => SetValue(TickBrushProperty, value);
    }

    // The value under x, clamped to the range.
    public double ValueAt(double x)
    {
        double travel = Bounds.Width - (2 * ThumbRadius);
        if (travel <= 0 || Maximum <= Minimum)
            return Minimum;

        double ratio = Math.Clamp((x - ThumbRadius) / travel, 0, 1);
        return Minimum + (ratio * (Maximum - Minimum));
    }

    // How much the value changes per device-independent pixel.
    public double ValuePerPixel
    {
        get
        {
            double travel = Bounds.Width - (2 * ThumbRadius);
            return travel > 0 ? (Maximum - Minimum) / travel : 0;
        }
    }

    internal double XOf(double value)
    {
        double travel = Math.Max(Bounds.Width - (2 * ThumbRadius), 0);
        double ratio = Maximum > Minimum ? Math.Clamp((value - Minimum) / (Maximum - Minimum), 0, 1) : 0;
        return ThumbRadius + (ratio * travel);
    }

    protected override Size MeasureOverride(Size availableSize) => new(2 * ThumbRadius, (2 * ThumbRadius) + 4);

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width <= 2 * ThumbRadius)
            return;

        double centerY = Bounds.Height / 2;
        var rail = new Rect(ThumbRadius, centerY - (RailThickness / 2), Bounds.Width - (2 * ThumbRadius), RailThickness);
        context.DrawRectangle(Background, null, new RoundedRect(rail, RailThickness / 2));

        double valueX = XOf(Value);
        double originX = XOf(Origin);
        if (Foreground is { } fill && Math.Abs(valueX - originX) > 0.5)
        {
            double left = Math.Min(valueX, originX);
            var part = new Rect(left, rail.Top, Math.Abs(valueX - originX), RailThickness);
            context.DrawRectangle(fill, null, new RoundedRect(part, RailThickness / 2));
        }

        // A tick marks the origin when it lies inside the rail.
        if (Origin > Minimum && Origin < Maximum && TickBrush is { } tick)
        {
            context.DrawRectangle(tick, null, new Rect(originX - 0.5, centerY - 5, 1, 10));
        }

        var thumbCenter = new Point(valueX, centerY);
        Pen? border = ThumbBorderBrush is { } borderBrush ? new Pen(borderBrush, 1) : null;
        context.DrawEllipse(ThumbBrush, border, thumbCenter, ThumbRadius - 0.5, ThumbRadius - 0.5);
    }
}
