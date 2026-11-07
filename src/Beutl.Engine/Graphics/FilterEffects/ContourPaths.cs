using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

/// <summary>
/// Builds Skia paths from the contours traced in a bitmap's alpha.
/// </summary>
internal static class ContourPaths
{
    public static void AddClosedPolyline(SKPathBuilder builder, ReadOnlySpan<PixelPoint> points)
    {
        for (int j = 0; j < points.Length; j++)
        {
            if (j == 0)
                builder.MoveTo(points[j].X, points[j].Y);
            else
                builder.LineTo(points[j].X, points[j].Y);
        }

        builder.Close();
    }

    public static void AddClosedPolyline(SKPathBuilder builder, ReadOnlySpan<Point> points, Vector offset)
    {
        builder.MoveTo(points[0].X + offset.X, points[0].Y + offset.Y);
        for (int i = 1; i < points.Length; i++)
            builder.LineTo(points[i].X + offset.X, points[i].Y + offset.Y);

        builder.Close();
    }

    /// <summary>
    /// The outline of <paramref name="src"/>'s alpha at sub-pixel precision, in its pixel space, as
    /// <see cref="AlphaOutlineTracer"/> traces it.
    /// </summary>
    public static SKPath CreateOutline(Bitmap src)
    {
        using var builder = new SKPathBuilder();
        foreach (Point[] contour in AlphaOutlineTracer.Trace(src))
            AddClosedPolyline(builder, contour, default);

        return builder.Detach();
    }
}
