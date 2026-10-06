using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

/// <summary>
/// Builds Skia paths from the contours <see cref="ContourTracer"/> traces in a bitmap's alpha.
/// </summary>
internal static class ContourPaths
{
    /// <summary>Appends <paramref name="points"/> to <paramref name="builder"/> as one closed polyline.</summary>
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

    /// <summary>Traces every contour of <paramref name="src"/>'s alpha into one path, each contour closed.</summary>
    public static SKPath CreateOutline(Bitmap src)
    {
        using var contours = ContourTracer.FindContours(src);

        using var builder = new SKPathBuilder();
        foreach (var contour in contours)
        {
            AddClosedPolyline(builder, contour.Span);
        }

        return builder.Detach();
    }
}
