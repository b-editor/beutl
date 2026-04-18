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

    private readonly float[] _left = new float[SampleWindow];
    private readonly float[] _right = new float[SampleWindow];

    public override void Render(DrawingContext context)
    {
        Rect bounds = new(Bounds.Size);
        context.FillRectangle(Brushes.Transparent, bounds);

        AudioSampleRingBuffer? buffer = RingBuffer;
        if (buffer == null || bounds.Width < 40 || bounds.Height < 40) return;

        int got = buffer.ReadAroundTime(PlayheadTime, _left, _right, SampleWindow);
        if (got <= 0) return;

        ReadOnlySpan<float> left = _left.AsSpan(0, got);
        ReadOnlySpan<float> right = _right.AsSpan(0, got);

        double size = Math.Min(bounds.Width, bounds.Height) - 20;
        var area = new Rect(
            bounds.Center.X - size / 2,
            bounds.Center.Y - size / 2,
            size,
            size);

        DrawGuides(context, area);
        DrawCorrelationLabel(context, area, ComputeCorrelation(left, right));
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

        IBrush textBrush = Foreground ?? Brushes.LightGray;
        DrawAxisLabel(context, "M", new Point(area.Center.X + 4, area.Top + 1), textBrush);
        DrawAxisLabel(context, "L", new Point(area.Left + 2, area.Center.Y - 14), textBrush);
        DrawAxisLabel(context, "R", new Point(area.Right - 12, area.Center.Y - 14), textBrush);
        DrawAxisLabel(context, "S", new Point(area.Center.X + 4, area.Bottom - 14), textBrush);
    }

    private void DrawCorrelationLabel(DrawingContext context, Rect area, float correlation)
    {
        string label = $"corr {correlation,5:+0.00;-0.00; 0.00}";
        IBrush brush = Foreground ?? Brushes.White;
        var text = new FormattedText(
            label,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            11,
            brush);
        context.DrawText(text, new Point(area.Right - text.Width, area.Bottom + 2));
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

    private static void DrawAxisLabel(DrawingContext context, string text, Point at, IBrush brush)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            10,
            brush);
        context.DrawText(formatted, at);
    }
}
