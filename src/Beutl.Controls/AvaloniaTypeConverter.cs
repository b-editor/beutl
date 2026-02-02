using Avalonia;

namespace Beutl.Controls;

public static class AvaloniaTypeConverter
{
    public static Avalonia.Media.Color ToAvaColor(this in Media.Color color)
    {
        return Avalonia.Media.Color.FromArgb(color.A, color.R, color.G, color.B);
    }

    public static Media.Color ToBtlColor(this in Avalonia.Media.Color color)
    {
        return Media.Color.FromArgb(color.A, color.R, color.G, color.B);
    }

    public static Matrix ToAvaMatrix(this in Graphics.Matrix matrix)
    {
        return new Matrix(
            matrix.M11, matrix.M12, matrix.M13,
            matrix.M21, matrix.M22, matrix.M23,
            matrix.M31, matrix.M32, matrix.M33);
    }

    public static Graphics.Matrix ToBtlMatrix(this in Matrix matrix)
    {
        return new Graphics.Matrix(
            (float)matrix.M11, (float)matrix.M12, (float)matrix.M13,
            (float)matrix.M21, (float)matrix.M22, (float)matrix.M23,
            (float)matrix.M31, (float)matrix.M32, (float)matrix.M33);
    }

    public static Point ToAvaPoint(this in Graphics.Point point)
    {
        return new Point(point.X, point.Y);
    }

    public static Graphics.Point ToBtlPoint(this in Point point)
    {
        return new Graphics.Point((float)point.X, (float)point.Y);
    }

    public static PixelPoint ToAvaPixelPoint(this in Media.PixelPoint point)
    {
        return new PixelPoint(point.X, point.Y);
    }

    public static Media.PixelPoint ToBtlPixelPoint(this in PixelPoint point)
    {
        return new Media.PixelPoint(point.X, point.Y);
    }

    public static Size ToAvaSize(this in Graphics.Size size)
    {
        return new Size(size.Width, size.Height);
    }

    public static Graphics.Size ToBtlSize(this in Size size)
    {
        return new Graphics.Size((float)size.Width, (float)size.Height);
    }

    public static PixelSize ToAvaPixelSize(this in Media.PixelSize size)
    {
        return new PixelSize(size.Width, size.Height);
    }

    public static Media.PixelSize ToBtlPixelSize(this in PixelSize size)
    {
        return new Media.PixelSize(size.Width, size.Height);
    }

    public static Rect ToAvaRect(this in Graphics.Rect rect)
    {
        return new Rect(rect.X, rect.Y, rect.Width, rect.Height);
    }

    public static Graphics.Rect ToBtlRect(this in Rect rect)
    {
        return new Graphics.Rect((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height);
    }

    public static PixelRect ToAvaPixelRect(this in Media.PixelRect rect)
    {
        return new PixelRect(rect.X, rect.Y, rect.Width, rect.Height);
    }

    public static Media.PixelRect ToBtlPixelRect(this in PixelRect rect)
    {
        return new Media.PixelRect(rect.X, rect.Y, rect.Width, rect.Height);
    }

    public static RelativePoint ToAvaRelativePoint(this in Graphics.RelativePoint pt)
    {
        return new RelativePoint(
            pt.Point.X,
            pt.Point.Y,
            pt.Unit == Graphics.RelativeUnit.Relative
                ? RelativeUnit.Relative
                : RelativeUnit.Absolute);
    }
}
