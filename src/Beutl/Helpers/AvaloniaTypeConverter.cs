namespace Beutl;

public static class AvaloniaTypeConverter
{
    public static Avalonia.Matrix ToAvaMatrix(this in Graphics.Matrix matrix)
    {
        return new(
            matrix.M11, matrix.M12, matrix.M13,
            matrix.M21, matrix.M22, matrix.M23,
            matrix.M31, matrix.M32, matrix.M33);
    }

    public static Graphics.Matrix ToBtlMatrix(this in Avalonia.Matrix matrix)
    {
        return new(
            (float)matrix.M11, (float)matrix.M12, (float)matrix.M13,
            (float)matrix.M21, (float)matrix.M22, (float)matrix.M23,
            (float)matrix.M31, (float)matrix.M32, (float)matrix.M33);
    }

    public static Avalonia.Point ToAvaPoint(this in Graphics.Point point)
    {
        return new(point.X, point.Y);
    }

    public static Graphics.Point ToBtlPoint(this in Avalonia.Point point)
    {
        return new((float)point.X, (float)point.Y);
    }

    public static Avalonia.PixelPoint ToAvaPixelPoint(this in Media.PixelPoint point)
    {
        return new(point.X, point.Y);
    }

    public static Media.PixelPoint ToBtlPixelPoint(this in Avalonia.PixelPoint point)
    {
        return new(point.X, point.Y);
    }

    public static Avalonia.Size ToAvaSize(this in Graphics.Size size)
    {
        return new(size.Width, size.Height);
    }

    public static Graphics.Size ToBtlSize(this in Avalonia.Size size)
    {
        return new((float)size.Width, (float)size.Height);
    }

    public static Avalonia.PixelSize ToAvaPixelSize(this in Media.PixelSize size)
    {
        return new(size.Width, size.Height);
    }

    public static Media.PixelSize ToBtlPixelSize(this in Avalonia.PixelSize size)
    {
        return new(size.Width, size.Height);
    }

    public static Avalonia.Rect ToAvaRect(this in Graphics.Rect rect)
    {
        return new(rect.X, rect.Y, rect.Width, rect.Height);
    }

    public static Graphics.Rect ToBtlRect(this in Avalonia.Rect rect)
    {
        return new((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height);
    }

    public static Avalonia.PixelRect ToAvaPixelRect(this in Media.PixelRect rect)
    {
        return new(rect.X, rect.Y, rect.Width, rect.Height);
    }

    public static Media.PixelRect ToBtlPixelRect(this in Avalonia.PixelRect rect)
    {
        return new(rect.X, rect.Y, rect.Width, rect.Height);
    }
}
