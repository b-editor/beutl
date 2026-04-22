using Avalonia;
using Avalonia.Media;
using Beutl.Editor.Components.AudioVisualizerTab.ViewModels;

namespace Beutl.Editor.Components.AudioVisualizerTab.Views;

// Stereo goniometer (Lissajous) rotated 45° so that pure mono content
// traces a vertical line and a fully out-of-phase signal traces a horizontal
// line — the convention used by broadcast monitoring tools. Includes a
// numeric correlation coefficient (-1..+1) so phase issues are quantifiable.
public sealed class PhaseScopeControl : AudioVisualizerControlBase
{
    private const int SampleWindow = 4096;
    private const double AxisLabelFontSize = 12.0;
    private const double CorrelationFontSize = 13.0;

    private readonly float[] _left = new float[SampleWindow];
    private readonly float[] _right = new float[SampleWindow];

    public override void Render(DrawingContext context)
    {
        Rect bounds = new(Bounds.Size);
        context.FillRectangle(Brushes.Transparent, bounds);

        AudioSampleRingBuffer? buffer = RingBuffer;
        if (buffer == null || bounds.Width < 40 || bounds.Height < 40) return;

        int got = buffer.ReadAroundTime(PlayheadTime, _left, _right, SampleWindow, out int leadingZeros);
        if (got <= 0) return;

        // Use only the real samples. Padded zeros would pile up as a bright dot
        // at the origin and skew the correlation readout toward 0.
        ReadOnlySpan<float> left = _left.AsSpan(leadingZeros, got);
        ReadOnlySpan<float> right = _right.AsSpan(leadingZeros, got);

        // Reserve enough margin outside the diamond for axis labels (M/L/R/S)
        // and the correlation readout. The previous 10 px margin clipped 12 pt
        // labels when they were positioned outside the vertices.
        const double outerMargin = 24;
        double size = Math.Min(bounds.Width, bounds.Height) - outerMargin * 2;
        if (size < 40) return;
        var area = new Rect(
            bounds.Center.X - size / 2,
            bounds.Center.Y - size / 2,
            size,
            size);

        DrawGuides(context, area);
        DrawCorrelationLabel(context, bounds, ComputeCorrelation(left, right));
        DrawSamples(context, area, left, right);
    }

    private void DrawGuides(DrawingContext context, Rect area)
    {
        IBrush guideBrush = new SolidColorBrush(Colors.Gray, 0.35);
        var pen = new Pen(guideBrush, 0.5);

        // Bounding diamond (45°-rotated square) — the limit of the rotated coordinate space.
        var diamond = new StreamGeometry();
        using (StreamGeometryContext ctx = diamond.Open())
        {
            ctx.BeginFigure(new Point(area.Center.X, area.Top), false);
            ctx.LineTo(new Point(area.Right, area.Center.Y));
            ctx.LineTo(new Point(area.Center.X, area.Bottom));
            ctx.LineTo(new Point(area.Left, area.Center.Y));
            ctx.EndFigure(true);
        }
        context.DrawGeometry(null, pen, diamond);

        // Vertical mono axis and horizontal anti-phase axis.
        context.DrawLine(pen, new Point(area.Center.X, area.Top), new Point(area.Center.X, area.Bottom));
        context.DrawLine(pen, new Point(area.Left, area.Center.Y), new Point(area.Right, area.Center.Y));

        IBrush textBrush = Brushes.LightGray;
        // Place labels just outside each diamond vertex so they never overlap
        // the dot cloud or the axis lines, and stay aligned regardless of font size.
        DrawAxisLabelCentered(context, "M", new Point(area.Center.X, area.Top - 2), textBrush, hAlign: 0.5, vAlign: 1.0);
        DrawAxisLabelCentered(context, "L", new Point(area.Left - 4, area.Center.Y), textBrush, hAlign: 1.0, vAlign: 0.5);
        DrawAxisLabelCentered(context, "R", new Point(area.Right + 4, area.Center.Y), textBrush, hAlign: 0.0, vAlign: 0.5);
        DrawAxisLabelCentered(context, "S", new Point(area.Center.X, area.Bottom + 2), textBrush, hAlign: 0.5, vAlign: 0.0);
    }

    private void DrawCorrelationLabel(DrawingContext context, Rect bounds, float correlation)
    {
        string label = $"corr {correlation,5:+0.00;-0.00; 0.00}";
        var text = new FormattedText(
            label,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            CorrelationFontSize,
            Brushes.White);
        // Anchor to the top-left of the control so it never collides with the
        // S vertex label below the diamond.
        context.DrawText(text, new Point(4, 2));
    }

    private void DrawSamples(DrawingContext context, Rect area, ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        // Project to the rotated frame:
        //   x = (L - R) / sqrt(2)  (stereo width)
        //   y = (L + R) / sqrt(2)  (mono content)
        // Then map [-1, 1] → area extents. Y flipped so positive mono points up.
        float scaleX = (float)(area.Width * 0.5);
        float scaleY = (float)(area.Height * 0.5);
        float cx = (float)area.Center.X;
        float cy = (float)area.Center.Y;
        const float invSqrt2 = 0.70710678f;

        IBrush brush = PrimaryBrush;
        // Sub-pixel-sized rectangles draw faster than per-sample DrawEllipse and
        // are visually indistinguishable in a goniometer.
        for (int i = 0; i < left.Length; i++)
        {
            float l = left[i];
            float r = right[i];
            float xRot = (l - r) * invSqrt2;
            float yRot = (l + r) * invSqrt2;

            double x = cx + xRot * scaleX;
            double y = cy - yRot * scaleY;
            context.FillRectangle(brush, new Rect(x - 0.5, y - 0.5, 1.2, 1.2));
        }
    }

    private static float ComputeCorrelation(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        double sumLR = 0, sumLL = 0, sumRR = 0;
        for (int i = 0; i < left.Length; i++)
        {
            float l = left[i];
            float r = right[i];
            sumLR += l * r;
            sumLL += l * l;
            sumRR += r * r;
        }
        double denom = Math.Sqrt(sumLL * sumRR);
        if (denom <= double.Epsilon) return 0f;
        return (float)Math.Clamp(sumLR / denom, -1.0, 1.0);
    }

    private static void DrawAxisLabelCentered(DrawingContext context, string text, Point at, IBrush brush, double hAlign, double vAlign)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            AxisLabelFontSize,
            brush);
        // hAlign/vAlign in [0,1] anchor the named edge of the label to `at`:
        //   0.0 = left/top, 0.5 = center, 1.0 = right/bottom of the label.
        double x = at.X - formatted.Width * hAlign;
        double y = at.Y - formatted.Height * vAlign;
        context.DrawText(formatted, new Point(x, y));
    }
}
