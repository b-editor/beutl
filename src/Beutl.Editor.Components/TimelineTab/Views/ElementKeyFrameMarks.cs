using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Beutl.Editor.Components.Helpers;

namespace Beutl.Editor.Components.TimelineTab.Views;

// The keyframe times of a clip's objects as small diamonds along its bottom edge, the shape the inline layers under the
// clip draw keys in. A diamond closer to the one before than its own width is left out, so a zoomed-out clip shows one
// where several keys are.
public sealed class ElementKeyFrameMarks : Control
{
    public static readonly StyledProperty<IReadOnlyList<MediaTime>?> TimesProperty =
        AvaloniaProperty.Register<ElementKeyFrameMarks, IReadOnlyList<MediaTime>?>(nameof(Times));

    public static readonly StyledProperty<float> ScaleProperty =
        AvaloniaProperty.Register<ElementKeyFrameMarks, float>(nameof(Scale), 1);

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<ElementKeyFrameMarks, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<ElementKeyFrameMarks, IBrush?>(nameof(Stroke));

    internal const double MarkSize = 7;

    private Pen? _pen;

    static ElementKeyFrameMarks()
    {
        AffectsRender<ElementKeyFrameMarks>(TimesProperty, ScaleProperty, FillProperty, StrokeProperty);
    }

    public ElementKeyFrameMarks()
    {
        IsHitTestVisible = false;
    }

    // The times from the element's start.
    public IReadOnlyList<MediaTime>? Times
    {
        get => GetValue(TimesProperty);
        set => SetValue(TimesProperty, value);
    }

    public float Scale
    {
        get => GetValue(ScaleProperty);
        set => SetValue(ScaleProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    // A one-pixel ring around each diamond, which keeps it apart from the thumbnails or waveform under it.
    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    // The center of each diamond drawn, from the clip's left edge. A key on an edge is drawn whole, just inside it, and a
    // key outside the clip is not drawn.
    internal static List<double> Layout(IReadOnlyList<MediaTime> times, float scale, double width)
    {
        var centers = new List<double>();
        const double half = MarkSize / 2;
        if (width < MarkSize) return centers;

        double last = double.NegativeInfinity;
        foreach (MediaTime time in times)
        {
            double x = time.TimeToPixel(scale);
            if (x < -0.5 || x > width + 0.5) continue;

            x = Math.Clamp(x, half, width - half);
            if (x - last < MarkSize) continue;

            centers.Add(x);
            last = x;
        }

        return centers;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Times is not { Count: > 0 } times || Fill is not { } fill) return;

        List<double> centers = Layout(times, Scale, Bounds.Width);
        if (centers.Count == 0) return;

        const double half = MarkSize / 2;
        double y = Bounds.Height / 2;
        var geometry = new StreamGeometry();
        using (StreamGeometryContext figures = geometry.Open())
        {
            foreach (double x in centers)
            {
                figures.BeginFigure(new Point(x, y - half), true);
                figures.LineTo(new Point(x + half, y));
                figures.LineTo(new Point(x, y + half));
                figures.LineTo(new Point(x - half, y));
                figures.EndFigure(true);
            }
        }

        // A pen two pixels wide, half of which the fill then covers, rings the diamond without making it smaller.
        if (GetRingPen() is { } ring)
            context.DrawGeometry(null, ring, geometry);
        context.DrawGeometry(fill, null, geometry);
    }

    private Pen? GetRingPen()
    {
        if (Stroke is not { } stroke) return null;
        if (_pen is null || !ReferenceEquals(_pen.Brush, stroke))
            _pen = new Pen(stroke, 2, lineJoin: PenLineJoin.Miter);
        return _pen;
    }
}
