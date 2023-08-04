using Beutl.Media;
using Beutl.Utilities;

namespace Beutl.Graphics.Rendering;

internal static class PenHelper
{
    public static Rect GetBounds(Rect rect, IPen? pen)
    {
        if (pen != null)
        {
            float thickness = pen.Thickness;
            rect = pen.StrokeAlignment switch
            {
                StrokeAlignment.Center => rect.Inflate(thickness / 2),
                StrokeAlignment.Outside => rect.Inflate(thickness),
                _ => rect,
            };
        }

        return rect;
    }

    public static float GetRealThickness(StrokeAlignment align, float thickness)
    {
        return align switch
        {
            StrokeAlignment.Inside => 0,
            StrokeAlignment.Center => thickness / 2,
            StrokeAlignment.Outside => thickness,
            _ => 0,
        };
    }

    public static Rect CalculateBoundsWithStrokeCap(Rect rect, IPen? pen)
    {
        if (pen == null || MathUtilities.IsZero(pen.Thickness)) return rect;

        return pen.StrokeCap switch
        {
            StrokeCap.Flat => rect,
            StrokeCap.Round => rect.Inflate(pen.Thickness / 2),
            StrokeCap.Square => rect.Inflate(pen.Thickness),
            _ => rect,
        };
    }
}
