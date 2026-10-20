using System.Diagnostics.CodeAnalysis;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.AudioVisualizers;

/// <summary>
/// The layout and path arithmetic the bar-drawing audio visualizer shapes share.
/// </summary>
internal static class BarGeometry
{
    /// <summary>Resolves the top and the height of bar <paramref name="index"/> of a waveform.</summary>
    public delegate (float Top, float Height) WaveformBar(
        in WaveformRenderContext context, int index, float centerY, float halfHeight);

    /// <summary>
    /// Resolves how wide one bar is drawn.
    /// </summary>
    /// <param name="requested">
    /// The configured width, or 0 to derive one that leaves a hairline gap between neighbouring slots.
    /// </param>
    /// <param name="slotWidth">The horizontal space one bar is allotted.</param>
    public static float ResolveWidth(float requested, float slotWidth)
        => requested > 0f ? MathF.Max(0.5f, requested) : MathF.Max(1f, slotWidth - 0.5f);

    /// <summary>
    /// Resolves the radius the innermost end of a radial bar starts at.
    /// </summary>
    /// <param name="requested">The configured inner radius.</param>
    /// <param name="outerRadius">The radius the longest bar reaches.</param>
    /// <remarks>
    /// The result is capped one unit below <paramref name="outerRadius"/> so a bar always has some radial
    /// span left to draw, and is floored at zero.
    /// </remarks>
    public static float ResolveInnerRadius(float requested, float outerRadius)
    {
        float innerRadius = MathF.Min(requested, outerRadius - 1f);
        return innerRadius < 0f ? 0f : innerRadius;
    }

    /// <summary>
    /// Builds the transform a radial bar is drawn under: it puts the origin on the circle of radius
    /// <paramref name="radius"/> around (<paramref name="centerX"/>, <paramref name="centerY"/>) at
    /// <paramref name="angleRad"/> and turns the x axis away from that centre, so the bar itself stays an
    /// axis-aligned rectangle growing from the origin.
    /// </summary>
    public static Matrix RadialBarTransform(float centerX, float centerY, float radius, float angleRad)
        => Matrix.CreateRotation(angleRad)
           * Matrix.CreateTranslation(
               centerX + radius * MathF.Cos(angleRad),
               centerY + radius * MathF.Sin(angleRad));

    /// <summary>
    /// Appends one bar with rounded corners to <paramref name="builder"/>.
    /// </summary>
    /// <remarks>
    /// Every radius is clamped to half the bar's shorter side, so a bar smaller than its corners still
    /// closes instead of folding through itself.
    /// </remarks>
    public static void AddRoundedBar(
        SKPathBuilder builder, float x, float y, float width, float height, in CornerRadius cornerRadius)
    {
        float maxRadius = MathF.Min(width, height) * 0.5f;
        float tl = MathF.Min(cornerRadius.TopLeft, maxRadius);
        float tr = MathF.Min(cornerRadius.TopRight, maxRadius);
        float br = MathF.Min(cornerRadius.BottomRight, maxRadius);
        float bl = MathF.Min(cornerRadius.BottomLeft, maxRadius);

        var rect = new SKRect(x, y, x + width, y + height);
        var radii = new SKPoint[4]
        {
            new SKPoint(tl, tl),
            new SKPoint(tr, tr),
            new SKPoint(br, br),
            new SKPoint(bl, bl),
        };
        using var roundRect = new SKRoundRect();
        roundRect.SetRectRadii(rect, radii);
        builder.AddRoundRect(roundRect);
    }

    /// <summary>
    /// Prepares the fill paint and the emptied path builder a shape batches its rounded bars into, creating
    /// either one on first use.
    /// </summary>
    public static void BeginRoundedBars(
        [NotNull] ref SKPaint? paint,
        [NotNull] ref SKPathBuilder? builder,
        ImmediateCanvas canvas,
        in Rect bounds,
        Brush.Resource fill)
    {
        paint ??= new SKPaint();
        VisualizerPaint.ConfigureFill(paint, canvas, bounds, fill);
        builder ??= new SKPathBuilder();
        builder.Reset();
    }

    /// <summary>
    /// Draws the rounded bars batched in <paramref name="builder"/> as one path.
    /// </summary>
    public static void DrawRoundedBars(ImmediateCanvas canvas, SKPathBuilder builder, SKPaint paint)
    {
        using SKPath path = builder.Detach();
        canvas.Canvas.DrawPath(path, paint);
    }

    /// <summary>
    /// Draws one bar per waveform slot, batched into one path when <paramref name="cornerRadius"/> rounds the
    /// corners and as plain rectangles otherwise.
    /// </summary>
    public static void DrawWaveformBars(
        in WaveformRenderContext context,
        ref SKPaint? paint,
        ref SKPathBuilder? builder,
        float requestedWidth,
        in CornerRadius cornerRadius,
        WaveformBar resolveBar)
    {
        ImmediateCanvas canvas = context.Canvas;
        Rect bounds = context.Bounds;
        Brush.Resource fill = context.Fill;

        int barCount = context.Mins.Length;
        if (barCount == 0) return;

        float width = (float)bounds.Width;
        float height = (float)bounds.Height;
        float halfHeight = height * 0.5f;
        float centerY = (float)bounds.Y + halfHeight;
        float slotWidth = width / barCount;
        float barWidth = ResolveWidth(requestedWidth, slotWidth);
        float offsetX = (slotWidth - barWidth) * 0.5f;

        bool round = !cornerRadius.IsEmpty;

        if (round)
        {
            BeginRoundedBars(ref paint, ref builder, canvas, bounds, fill);
        }

        for (int i = 0; i < barCount; i++)
        {
            (float topY, float barHeight) = resolveBar(in context, i, centerY, halfHeight);
            float x = (float)bounds.X + i * slotWidth + offsetX;

            if (round)
            {
                AddRoundedBar(builder!, x, topY, barWidth, barHeight, cornerRadius);
            }
            else
            {
                canvas.DrawRectangle(new Rect(x, topY, barWidth, barHeight), fill, null);
            }
        }

        if (round)
        {
            DrawRoundedBars(canvas, builder!, paint!);
        }
    }
}
