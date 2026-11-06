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

    /// <summary>
    /// The outline of <paramref name="src"/>'s alpha at sub-pixel precision, in its pixel space, as
    /// <see cref="AlphaOutlineTracer"/> traces it.
    /// </summary>
    public static SKPath CreateOutline(Bitmap src)
    {
        using var builder = new SKPathBuilder();
        foreach (Point[] contour in AlphaOutlineTracer.Trace(src))
        {
            builder.MoveTo(contour[0].X, contour[0].Y);
            for (int i = 1; i < contour.Length; i++)
                builder.LineTo(contour[i].X, contour[i].Y);

            builder.Close();
        }

        return builder.Detach();
    }
}
