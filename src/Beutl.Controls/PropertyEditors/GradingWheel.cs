using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;

namespace Beutl.Controls.PropertyEditors;

public class GradingWheel : Thumb
{
    public static readonly StyledProperty<double> TickOffsetProperty =
        AvaloniaProperty.Register<GradingWheel, double>(nameof(TickOffset), 0.0);

    private const int TickCount = 40;
    private const double TickSpacing = 0.025;

    private double _dragStartTickOffset;

    static GradingWheel()
    {
        AffectsRender<GradingWheel>(TickOffsetProperty);
    }

    public double TickOffset
    {
        get => GetValue(TickOffsetProperty);
        set => SetValue(TickOffsetProperty, value);
    }

    protected override void OnDragStarted(VectorEventArgs e)
    {
        base.OnDragStarted(e);
        _dragStartTickOffset = TickOffset;
    }

    protected override void OnDragDelta(VectorEventArgs e)
    {
        base.OnDragDelta(e);

        if (Bounds.Width == 0)
            return;

        double delta = e.Vector.X / Bounds.Width;
        TickOffset = (_dragStartTickOffset + delta) % 1.0;
        if (TickOffset < 0)
            TickOffset += 1.0;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = new Rect(Bounds.Size);
        double cornerRadius = 4;

        // Background
        IBrush? backgroundBrush = Background ?? Brushes.Transparent;
        using (context.PushClip(new RoundedRect(bounds, cornerRadius)))
        {
            context.DrawRectangle(backgroundBrush, null, bounds);

            // Draw tick marks
            DrawTickMarks(context, bounds);
        }

        // Border
        IBrush? borderBrush = BorderBrush;
        if (borderBrush != null)
        {
            double thickness = BorderThickness.Left;
            if (thickness > 0)
            {
                Pen borderPen = new Pen(borderBrush, thickness);
                context.DrawRectangle(null, borderPen, new RoundedRect(bounds.Deflate(thickness / 2), cornerRadius));
            }
        }
    }

    private void DrawTickMarks(DrawingContext context, Rect bounds)
    {
        double centerX = bounds.Width / 2;
        double halfWidth = bounds.Width / 2;

        IBrush tickBrush = Foreground ?? Brushes.White;
        double tickWidth = 1.5;
        double tickHeight = bounds.Height * 0.6;
        double tickTop = (bounds.Height - tickHeight) / 2;

        for (int i = 0; i < TickCount; i++)
        {
            double normalizedPos = (i - TickCount / 2) * TickSpacing + TickOffset;

            // Wrap around
            while (normalizedPos > 0.5) normalizedPos -= 1.0;
            while (normalizedPos < -0.5) normalizedPos += 1.0;

            double x = centerX + normalizedPos * bounds.Width;

            // Skip if outside bounds
            if (x < 0 || x > bounds.Width)
                continue;

            // Calculate opacity based on distance from center (1.0 at center, 0.0 at edges)
            double distanceFromCenter = Math.Abs(x - centerX);
            double opacity = 1.0 - (distanceFromCenter / halfWidth);
            opacity = Math.Clamp(opacity, 0.0, 1.0);

            // Apply easing for smoother fade
            opacity = opacity * opacity;

            if (opacity > 0.01)
            {
                using (context.PushOpacity(opacity))
                {
                    Rect tickRect = new Rect(x - tickWidth / 2, tickTop, tickWidth, tickHeight);
                    context.DrawRectangle(tickBrush, null, tickRect, tickWidth / 2, tickWidth / 2);
                }
            }
        }
    }
}
