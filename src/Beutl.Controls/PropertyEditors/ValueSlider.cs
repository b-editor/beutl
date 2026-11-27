using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Beutl.Controls.PropertyEditors;

// A slim slider drawn in one piece: a rail, the part between Origin and Value filled, and a round thumb. It takes no
// focus or keys, and leaves its pointer to the editor that owns it, as NumberSliderEditor does.
public partial class ValueSlider : Control
{
    internal const double ThumbRadius = 6;
    private const double RailThickness = 4;

    static ValueSlider()
    {
        AffectsRender<ValueSlider>(
            MinimumProperty, MaximumProperty, ValueProperty, OriginProperty,
            BackgroundProperty, ForegroundProperty, ThumbBrushProperty, ThumbBorderBrushProperty, TickBrushProperty);
        FocusableProperty.OverrideDefaultValue<ValueSlider>(false);
    }

    [GeneratedStyledProperty]
    public partial double Minimum { get; set; }

    [GeneratedStyledProperty(DefaultValue = 1)]
    public partial double Maximum { get; set; }

    [GeneratedStyledProperty]
    public partial double Value { get; set; }

    // Where the fill starts, such as the property's default.
    [GeneratedStyledProperty]
    public partial double Origin { get; set; }

    [GeneratedStyledProperty(AddOwnerFrom = typeof(Border))]
    public partial IBrush? Background { get; set; }

    [GeneratedStyledProperty]
    public partial IBrush? Foreground { get; set; }

    [GeneratedStyledProperty]
    public partial IBrush? ThumbBrush { get; set; }

    [GeneratedStyledProperty]
    public partial IBrush? ThumbBorderBrush { get; set; }

    // The mark at the origin.
    [GeneratedStyledProperty]
    public partial IBrush? TickBrush { get; set; }

    // The value under x, clamped to the range.
    public double ValueAt(double x) => SliderTrack.ValueAt(x, Minimum, Maximum, ThumbRadius, Travel);

    // How much the value changes per device-independent pixel.
    public double ValuePerPixel => Travel > 0 ? (Maximum - Minimum) / Travel : 0;

    // Whether a click on the slider edits the number as text, so that only a drag moves the value.
    internal virtual bool EditsOnClick => false;

    // How far the thumb's center travels from Minimum to Maximum.
    private double Travel => Bounds.Width - (2 * ThumbRadius);

    internal double XOf(double value) => SliderTrack.XOf(value, Minimum, Maximum, ThumbRadius, Travel);

    protected override Size MeasureOverride(Size availableSize) => new(2 * ThumbRadius, (2 * ThumbRadius) + 4);

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width <= 2 * ThumbRadius)
            return;

        double centerY = Bounds.Height / 2;
        var rail = new Rect(ThumbRadius, centerY - (RailThickness / 2), Travel, RailThickness);
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
        // An immutable pen skips the compositor's per-frame resources, which a drag redraws every frame.
        var border = ThumbBorderBrush is { } borderBrush ? new ImmutablePen(borderBrush.ToImmutable(), 1) : null;
        context.DrawEllipse(ThumbBrush, border, thumbCenter, ThumbRadius - 0.5, ThumbRadius - 0.5);
    }
}

// The mapping between a value and an x along a track, which starts at left and runs over width; ValueSlider and
// TonalRangeSlider share it.
internal static class SliderTrack
{
    public static double XOf(double value, double minimum, double maximum, double left, double width)
    {
        double ratio = maximum > minimum ? Math.Clamp((value - minimum) / (maximum - minimum), 0, 1) : 0;
        return left + (ratio * Math.Max(width, 0));
    }

    // The value under x, clamped to the range; the minimum on a track with no length or no range.
    public static double ValueAt(double x, double minimum, double maximum, double left, double width)
    {
        if (width <= 0 || maximum <= minimum)
            return minimum;

        double ratio = Math.Clamp((x - left) / width, 0, 1);
        return minimum + (ratio * (maximum - minimum));
    }
}
