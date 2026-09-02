using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.AudioVisualizers;

/// <summary>
/// The layout and path arithmetic the bar-drawing audio visualizer shapes share.
/// </summary>
internal static class BarGeometry
{
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
    /// Appends one bar with rounded corners to <paramref name="path"/>.
    /// </summary>
    /// <remarks>
    /// Every radius is clamped to half the bar's shorter side, so a bar smaller than its corners still
    /// closes instead of folding through itself.
    /// </remarks>
    public static void AddRoundedBar(
        SKPath path, float x, float y, float width, float height, in CornerRadius cornerRadius)
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
        path.AddRoundRect(roundRect);
    }
}
