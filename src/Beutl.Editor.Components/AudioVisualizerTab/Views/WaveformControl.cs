using Avalonia;
using Avalonia.Media;
using Beutl.Editor.Components.AudioVisualizerTab.ViewModels;

namespace Beutl.Editor.Components.AudioVisualizerTab.Views;

public sealed class WaveformControl : AudioVisualizerControlBase
{
    private const int DisplaySamples = 2048;
    private readonly float[] _left = new float[DisplaySamples];
    private readonly float[] _right = new float[DisplaySamples];

    public override void Render(DrawingContext context)
    {
        Rect bounds = new(Bounds.Size);
        context.FillRectangle(Brushes.Transparent, bounds);

        AudioSampleRingBuffer? buffer = RingBuffer;
        if (buffer == null || bounds.Width < 4 || bounds.Height < 4) return;

        int got = buffer.ReadAroundTime(PlayheadTime, _left, _right, DisplaySamples);
        if (got <= 0) return;

        double halfHeight = bounds.Height / 2.0;
        double midTop = halfHeight / 2.0;
        double midBottom = halfHeight + halfHeight / 2.0;

        DrawChannel(context, _left.AsSpan(0, got), midTop, halfHeight, PrimaryBrush);
        DrawChannel(context, _right.AsSpan(0, got), midBottom, halfHeight, SecondaryBrush);

        // Center axis lines
        IBrush axisBrush = Foreground ?? Brushes.Gray;
        var axisPen = new Pen(axisBrush, 0.5) { DashStyle = DashStyle.Dash };
        context.DrawLine(axisPen, new Point(0, midTop), new Point(bounds.Width, midTop));
        context.DrawLine(axisPen, new Point(0, midBottom), new Point(bounds.Width, midBottom));
    }

    private void DrawChannel(DrawingContext context, ReadOnlySpan<float> samples, double centerY, double areaHeight, IBrush brush)
    {
        int count = samples.Length;
        if (count < 2) return;

        double width = Bounds.Width;
        double scale = (areaHeight / 2.0) * 0.9;
        double step = width / (count - 1);

        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(0, centerY - ClampSample(samples[0]) * scale), false);
            for (int i = 1; i < count; i++)
            {
                ctx.LineTo(new Point(i * step, centerY - ClampSample(samples[i]) * scale));
            }
            ctx.EndFigure(false);
        }

        var pen = new Pen(brush, 1.1, lineJoin: PenLineJoin.Round);
        context.DrawGeometry(null, pen, geometry);
    }

    private static float ClampSample(float v)
    {
        if (v > 1f) return 1f;
        if (v < -1f) return -1f;
        return v;
    }
}
