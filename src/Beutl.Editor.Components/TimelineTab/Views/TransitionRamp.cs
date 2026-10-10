using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Beutl.Animation.Easings;
using Beutl.Graphics.Transitions;

namespace Beutl.Editor.Components.TimelineTab.Views;

/// <summary>
/// Draws an element's part of a clip boundary transition: how far the transition has carried the picture
/// over to the incoming side, plotted over time. The part covers the span of the transition from
/// <see cref="StartFraction"/> to <see cref="EndFraction"/> of its duration, and the curve follows the
/// transition's <see cref="Easing"/>. The parts drawn by the two elements of a boundary join into one curve
/// across the cut.
/// </summary>
public sealed class TransitionRamp : Control
{
    // Enough segments that a curve reads as smooth at any width a part is drawn at.
    private const double PixelsPerSegment = 2;
    private const int MinimumSegments = 8;

    public static readonly StyledProperty<double> StartFractionProperty =
        AvaloniaProperty.Register<TransitionRamp, double>(nameof(StartFraction));

    public static readonly StyledProperty<double> EndFractionProperty =
        AvaloniaProperty.Register<TransitionRamp, double>(nameof(EndFraction), 1);

    public static readonly StyledProperty<Easing?> EasingProperty =
        AvaloniaProperty.Register<TransitionRamp, Easing?>(nameof(Easing));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<TransitionRamp, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        AvaloniaProperty.Register<TransitionRamp, IBrush?>(nameof(Background));

    static TransitionRamp()
    {
        AffectsRender<TransitionRamp>(
            StartFractionProperty, EndFractionProperty, EasingProperty, FillProperty, BackgroundProperty);
    }

    // How far into the transition's duration the part starts, from 0 to 1.
    public double StartFraction
    {
        get => GetValue(StartFractionProperty);
        set => SetValue(StartFractionProperty, value);
    }

    // How far into the transition's duration the part ends, from 0 to 1.
    public double EndFraction
    {
        get => GetValue(EndFractionProperty);
        set => SetValue(EndFractionProperty, value);
    }

    public Easing? Easing
    {
        get => GetValue(EasingProperty);
        set => SetValue(EasingProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    // The top edge of the filled area, left to right: at each x, the progress the transition has reached
    // at that moment, measured up from the bottom.
    internal static Point[] CreateCurve(Size size, double startFraction, double endFraction, Easing? easing)
    {
        double start = Math.Clamp(startFraction, 0, 1);
        double end = Math.Clamp(endFraction, 0, 1);
        int segments = easing is null or LinearEasing
            ? 1
            : Math.Max(MinimumSegments, (int)Math.Ceiling(size.Width / PixelsPerSegment));
        var points = new Point[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            double t = (double)i / segments;
            float progress = ClipTransition.Ease(easing, (float)(start + ((end - start) * t)));
            points[i] = new Point(size.Width * t, size.Height * (1 - progress));
        }

        return points;
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        if (Background is { } background)
        {
            context.FillRectangle(background, bounds);
        }

        if (Fill is not { } fill) return;

        Point[] curve = CreateCurve(bounds.Size, StartFraction, EndFraction, Easing);
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(0, bounds.Height), true);
            foreach (Point point in curve)
            {
                ctx.LineTo(point);
            }

            ctx.LineTo(new Point(bounds.Width, bounds.Height));
            ctx.EndFigure(true);
        }

        context.DrawGeometry(fill, null, geometry);
    }
}
