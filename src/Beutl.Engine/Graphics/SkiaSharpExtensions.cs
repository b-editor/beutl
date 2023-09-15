
using Beutl.Media;

using SkiaSharp;

namespace Beutl.Graphics;

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

    public static Point ToGraphicsPoint(this in SKPoint p)
    {
        return new Point(p.X, p.Y);
    }

    public static SKPoint ToSKPoint(this Vector p)
    {
        return new SKPoint(p.X, p.Y);
    }
    
    public static SKPointI ToSKPointI(this PixelPoint p)
    {
        return new SKPointI(p.X, p.Y);
    }

    public static SKRect ToSKRect(this in Rect r)
    {
        return new SKRect(r.X, r.Y, r.Right, r.Bottom);
    }

    public static SKRectI ToSKRectI(this in PixelRect r)
    {
        return new SKRectI(r.X, r.Y, r.Right, r.Bottom);
    }

    public static SKSize ToSKSize(this in Size s)
    {
        return new SKSize(s.Width, s.Height);
    }

    public static SKSizeI ToSKSizeI(this in PixelSize s)
    {
        return new SKSizeI(s.Width, s.Height);
    }

    public static Rect ToGraphicsRect(this in SKRect r)
    {
        return new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }

    public static Size ToGraphicsSize(this in SKSize s)
    {
        return new Size(s.Width, s.Height);
    }
    
    public static PixelSize ToGraphicsSize(this in SKSizeI s)
    {
        return new PixelSize(s.Width, s.Height);
    }

    public static SKMatrix ToSKMatrix(this in Matrix m)
    {
        var sm = new SKMatrix
        {
            ScaleX = m.M11,
            SkewX = m.M21,
            TransX = m.M31,
            SkewY = m.M12,
            ScaleY = m.M22,
            TransY = m.M32,
            Persp0 = m.M13,
            Persp1 = m.M23,
            Persp2 = m.M33
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

    public static Matrix ToMatrix(this in SKMatrix m)
    {
        return new Matrix(
            m.ScaleX, m.SkewY, m.Persp0,
            m.SkewX, m.ScaleY, m.Persp1,
            m.TransX, m.TransY, m.Persp2);
    }

    public static FontMetrics ToFontMetrics(this in SKFontMetrics metrics)
    {
        return new FontMetrics
        {
            Leading = metrics.Leading,
            CapHeight = metrics.CapHeight,
            XHeight = metrics.XHeight,
            XMax = metrics.XMax,
            XMin = metrics.XMin,
            MaxCharacterWidth = metrics.MaxCharacterWidth,
            AverageCharacterWidth = metrics.AverageCharacterWidth,
            Bottom = metrics.Bottom,
            Descent = metrics.Descent,
            Ascent = metrics.Ascent,
            Top = metrics.Top,
        };
    }

    public static FontStyle ToFontStyle(this SKFontStyleSlant slant)
    {
        return slant switch
        {
            SKFontStyleSlant.Upright => FontStyle.Normal,
            SKFontStyleSlant.Italic => FontStyle.Italic,
            SKFontStyleSlant.Oblique => FontStyle.Oblique,
            _ => throw new ArgumentOutOfRangeException(nameof(slant), slant, null)
        };
    }
}
