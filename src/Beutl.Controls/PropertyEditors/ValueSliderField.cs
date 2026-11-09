using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Beutl.Controls.PropertyEditors;

// A ValueSlider laid over a number's text box, which makes the box the slider: the box fills up to the value and a slim
// upright thumb stands at it, for a number with a range to show. Its theme keeps it see-through until the pointer comes,
// so that a row at rest looks like any other box. A click on it edits the text, and only a drag moves the value.
public class ValueSliderField : ValueSlider
{
    public static readonly StyledProperty<CornerRadius> CornerRadiusProperty =
        Border.CornerRadiusProperty.AddOwner<ValueSliderField>();

    public static readonly StyledProperty<IBrush?> BorderBrushProperty =
        Border.BorderBrushProperty.AddOwner<ValueSliderField>();

    public static readonly StyledProperty<bool> ShowsValueProperty =
        AvaloniaProperty.Register<ValueSliderField, bool>(nameof(ShowsValue), true);

    private const double BorderThickness = 1;
    private const double ThumbWidth = 3;
    private const double ThumbHeightRatio = 0.6;

    static ValueSliderField()
    {
        AffectsRender<ValueSliderField>(CornerRadiusProperty, BorderBrushProperty, ShowsValueProperty);
    }

    public CornerRadius CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public IBrush? BorderBrush
    {
        get => GetValue(BorderBrushProperty);
        set => SetValue(BorderBrushProperty, value);
    }

    // Whether the fill and the thumb show where the value sits in the range.
    public bool ShowsValue
    {
        get => GetValue(ShowsValueProperty);
        set => SetValue(ShowsValueProperty, value);
    }

    internal override bool EditsOnClick => true;

    public override void Render(DrawingContext context)
    {
        Size size = Bounds.Size;
        if (size.Width <= 2 * ThumbRadius)
            return;

        var outer = new Rect(size);
        double radius = CornerRadius.TopLeft;
        var box = new RoundedRect(outer.Deflate(BorderThickness), Math.Max(radius - BorderThickness, 0));
        // Drawn even when transparent, since the pointer reaches only what is drawn and this takes it across the box.
        context.DrawRectangle(Background ?? Brushes.Transparent, null, box);

        if (ShowsValue)
            DrawValue(context, box);

        if (BorderBrush is { } border)
        {
            // An immutable pen skips the compositor's per-frame resources, which a drag redraws every frame.
            var pen = new ImmutablePen(border.ToImmutable(), BorderThickness);
            var stroke = new RoundedRect(outer.Deflate(BorderThickness / 2), Math.Max(radius - (BorderThickness / 2), 0));
            context.DrawRectangle(null, pen, stroke);
        }
    }

    private void DrawValue(DrawingContext context, RoundedRect box)
    {
        Size size = Bounds.Size;
        double value = double.IsFinite(Value) ? Value : Minimum;
        // Not from Origin, the property's default, from which an opacity of 100 would fill nothing: a range around zero
        // fills from zero, either way, and any other from its minimum.
        double origin = Minimum < 0 && Maximum > 0 ? 0 : Minimum;
        double left = FillX(Math.Min(origin, value));
        double right = FillX(Math.Max(origin, value));
        if (Foreground is { } fill && right > left)
        {
            using (context.PushClip(box))
                context.DrawRectangle(fill, null, new Rect(left, 0, right - left, size.Height));
        }

        double thumbHeight = Math.Round(size.Height * ThumbHeightRatio);
        var thumb = new Rect(
            Math.Round(XOf(value) - (ThumbWidth / 2)), Math.Round((size.Height - thumbHeight) / 2), ThumbWidth, thumbHeight);
        context.DrawRectangle(ThumbBrush, null, new RoundedRect(thumb, ThumbWidth / 2));
    }

    // Where the fill ends for value; at either end of the range that is the box's edge, so that no sliver stays outside
    // the thumb's travel.
    private double FillX(double value)
    {
        if (value <= Minimum)
            return 0;

        return value >= Maximum ? Bounds.Width : XOf(value);
    }
}
