using System.Numerics;

using BEditorNext.Media;

using SkiaSharp;

namespace BEditorNext.Graphics;

internal static class StructExtensions
{
    public static SKPoint ToSkia(this in Point point)
    {
        return new SKPoint(point.X, point.Y);
    }

    public static SKPointI ToSkia(this in PixelPoint point)
    {
        return new SKPointI(point.X, point.Y);
    }

    public static SKRect ToSkia(this in Rect rect)
    {
        return SKRect.Create(rect.X, rect.Y, rect.Width, rect.Height);
    }

    public static SKRectI ToSkia(this in PixelRect rect)
    {
        return SKRectI.Create(rect.X, rect.Y, rect.Width, rect.Height);
    }

    public static SKSize ToSkia(this in Size size)
    {
        return new SKSize(size.Width, size.Height);
    }

    public static SKSizeI ToSkia(this in PixelSize size)
    {
        return new SKSizeI(size.Width, size.Height);
    }

    public static Point ToPoint(this in SKPoint point)
    {
        return new Point(point.X, point.Y);
    }

    public static PixelPoint ToPixelPoint(this in SKPointI point)
    {
        return new PixelPoint(point.X, point.Y);
    }

    public static Rect ToRect(this in SKRect rect)
    {
        return new Rect(rect.Left, rect.Top, rect.Width, rect.Height);
    }

    public static PixelRect ToPixelRect(this in SKRectI rect)
    {
        return new PixelRect(rect.Left, rect.Top, rect.Width, rect.Height);
    }

    public static Size ToSize(this in SKSize size)
    {
        return new Size(size.Width, size.Height);
    }

    public static PixelSize ToPixelSize(this in SKSizeI size)
    {
        return new PixelSize(size.Width, size.Height);
    }

    public static SKMatrix ToSKMatrix(this in Matrix3x2 m)
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

    public static Matrix3x2 ToMatrix3x2(this in SKMatrix m)
    {
        var sm = new Matrix3x2
        {
            M11 = m.ScaleX,
            M21 = m.SkewX,
            M31 = m.TransX,
            M12 = m.SkewY,
            M22 = m.ScaleY,
            M32 = m.TransY,
        };

        return sm;
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
