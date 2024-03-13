namespace Beutl.Controls;

internal static class AvaloniaTypeConverter
{
    public static Avalonia.Media.Color ToAvaColor(this in Media.Color color)
    {
        return Avalonia.Media.Color.FromArgb(color.A, color.R, color.G, color.B);
    }

    public static Media.Color ToBtlColor(this in Avalonia.Media.Color color)
    {
        return Media.Color.FromArgb(color.A, color.R, color.G, color.B);
    }

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

    public static Avalonia.RelativePoint ToAvaRelativePoint(this in Graphics.RelativePoint pt)
    {
        return new(
            pt.Point.X,
            pt.Point.Y,
            pt.Unit == Graphics.RelativeUnit.Relative
                ? Avalonia.RelativeUnit.Relative
                : Avalonia.RelativeUnit.Absolute);
    }

    public static Avalonia.Media.IBrush ToAvaBrush(this Media.IBrush brush)
    {
        switch (brush)
        {
            case Media.ISolidColorBrush s:
                return new Avalonia.Media.SolidColorBrush
                {
                    Color = s.Color.ToAvaColor(),
                    Opacity = s.Opacity
                };
            case Media.IGradientBrush g:
                {
                    var sp = g.SpreadMethod switch
                    {
                        Media.GradientSpreadMethod.Pad => Avalonia.Media.GradientSpreadMethod.Pad,
                        Media.GradientSpreadMethod.Reflect => Avalonia.Media.GradientSpreadMethod.Reflect,
                        Media.GradientSpreadMethod.Repeat => Avalonia.Media.GradientSpreadMethod.Repeat,
                        _ => throw new ArgumentOutOfRangeException(),
                    };
                    var stops = new Avalonia.Media.GradientStops();
                    stops.AddRange(g.GradientStops.Select(v => new Avalonia.Media.GradientStop(v.Color.ToAvaColor(), v.Offset)));

                    switch (g)
                    {
                        case Media.ILinearGradientBrush l:
                            {
                                var st = l.StartPoint;
                                var ed = l.EndPoint;
                                return new Avalonia.Media.LinearGradientBrush
                                {
                                    StartPoint = st.ToAvaRelativePoint(),
                                    EndPoint = ed.ToAvaRelativePoint(),
                                    GradientStops = stops,
                                    Opacity = l.Opacity
                                };
                            }

                        case Media.IConicGradientBrush c:
                            {
                                var center = c.Center;
                                return new Avalonia.Media.ConicGradientBrush
                                {
                                    Center = center.ToAvaRelativePoint(),
                                    Angle = c.Angle,
                                    GradientStops = stops,
                                    Opacity = c.Opacity
                                };
                            }

                        case Media.IRadialGradientBrush r:
                            {
                                var center = r.Center;
                                var origin = r.GradientOrigin;
                                return new Avalonia.Media.RadialGradientBrush
                                {
                                    Center = center.ToAvaRelativePoint(),
                                    GradientOrigin = origin.ToAvaRelativePoint(),
                                    RadiusX = new Avalonia.RelativeScalar(r.Radius, Avalonia.RelativeUnit.Relative),
                                    RadiusY = new Avalonia.RelativeScalar(r.Radius, Avalonia.RelativeUnit.Relative),
                                    GradientStops = stops,
                                    Opacity = r.Opacity
                                };

                            }
                    }
                }
                break;
        }

        return null;
    }
}
