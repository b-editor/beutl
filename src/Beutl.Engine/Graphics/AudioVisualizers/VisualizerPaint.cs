using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.AudioVisualizers;

/// <summary>
/// Configures the <see cref="SKPaint"/> an audio visualizer shape reuses between frames.
/// </summary>
internal static class VisualizerPaint
{
    /// <summary>
    /// Configures <paramref name="paint"/> to fill with the visualizer's brush.
    /// </summary>
    public static void ConfigureFill(SKPaint paint, ImmediateCanvas canvas, in Rect bounds, Brush.Resource fill)
    {
        canvas.CreateBrushConstructor(bounds, fill, BlendMode.SrcOver).ConfigurePaint(paint);
        paint.Style = SKPaintStyle.Fill;
    }

    /// <summary>
    /// Configures <paramref name="paint"/> to stroke with the visualizer's brush at
    /// <paramref name="thickness"/>, rounding the caps and joins so a polyline reads as one curve.
    /// </summary>
    public static void ConfigureStroke(
        SKPaint paint, ImmediateCanvas canvas, in Rect bounds, Brush.Resource fill, float thickness)
    {
        canvas.CreateBrushConstructor(bounds, fill, BlendMode.SrcOver).ConfigurePaint(paint);
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeCap = SKStrokeCap.Round;
        paint.StrokeJoin = SKStrokeJoin.Round;
        paint.StrokeWidth = thickness;
    }
}
