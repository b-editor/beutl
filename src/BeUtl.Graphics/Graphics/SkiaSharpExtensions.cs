
using BeUtl.Media;

using SkiaSharp;

namespace BeUtl.Graphics;

internal static class SkiaSharpExtensions
{
    public static SKFilterQuality ToSKFilterQuality(this BitmapInterpolationMode interpolationMode)
    {
        return interpolationMode switch
        {
            BitmapInterpolationMode.LowQuality => SKFilterQuality.Low,
            BitmapInterpolationMode.MediumQuality => SKFilterQuality.Medium,
            BitmapInterpolationMode.HighQuality => SKFilterQuality.High,
            BitmapInterpolationMode.Default => SKFilterQuality.None,
            _ => throw new ArgumentOutOfRangeException(nameof(interpolationMode), interpolationMode, null),
        };
    }

    public static SKPoint ToSKPoint(this Point p)
    {
        return new SKPoint(p.X, p.Y);
    }

    public static SKPoint ToSKPoint(this Vector p)
    {
        return new SKPoint(p.X, p.Y);
    }

    public static SKRect ToSKRect(this Rect r)
    {
        return new SKRect(r.X, r.Y, r.Right, r.Bottom);
    }

    public static Rect ToGraphicsRect(this SKRect r)
    {
        return new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }

    public static SKMatrix ToSKMatrix(this Matrix m)
    {
        var sm = new SKMatrix
        {
            ScaleX = m.M11,
            SkewX = m.M21,
            TransX = m.M31,
            SkewY = m.M12,
            ScaleY = m.M22,
            TransY = m.M32,
            Persp0 = 0,
            Persp1 = 0,
            Persp2 = 1
        };

        return sm;
    }

    public static SKColor ToSKColor(this Color c)
    {
        return new SKColor(c.R, c.G, c.B, c.A);
    }

    public static SKShaderTileMode ToSKShaderTileMode(this GradientSpreadMethod m)
    {
        return m switch
        {
            GradientSpreadMethod.Reflect => SKShaderTileMode.Mirror,
            GradientSpreadMethod.Repeat => SKShaderTileMode.Repeat,
            _ => SKShaderTileMode.Clamp,
        };
    }

    public static SKClipOperation ToSKClipOperation(this ClipOperation operation)
    {
        return operation switch
        {
            ClipOperation.Difference => SKClipOperation.Difference,
            ClipOperation.Intersect => SKClipOperation.Intersect,
            _ => SKClipOperation.Intersect,
        };
    }
}
