using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.Converters;

public sealed class PointConverter : TypeConverter
{
    public override bool CanConvertTo(ITypeDescriptorContext? context, [NotNullWhen(true)] Type? destinationType)
    {
        return destinationType == typeof(float[])
            || destinationType == typeof(Tuple<float, float>)
            || destinationType == typeof(Rect)
            || destinationType == typeof(Size)
            || destinationType == typeof(PixelPoint)
            || destinationType == typeof(PixelRect)
            || destinationType == typeof(PixelSize)
            || base.CanConvertTo(context, destinationType);
    }

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        if (value is Point point)
        {
            if (destinationType == typeof(float[]))
            {
                return new float[] { point.X, point.Y };
            }
            else if (destinationType == typeof(Tuple<float, float>))
            {
                return new Tuple<float, float>(point.X, point.Y);
            }
            else if (destinationType == typeof(Rect))
            {
                return new Rect(point.X, point.Y, 0, 0);
            }
            else if (destinationType == typeof(Size))
            {
                return new Size(point.X, point.Y);
            }
            else if (destinationType == typeof(PixelPoint))
            {
                return new PixelPoint((int)point.X, (int)point.Y);
            }
            else if (destinationType == typeof(PixelRect))
            {
                return new PixelRect((int)point.X, (int)point.Y, 0, 0);
            }
            else if (destinationType == typeof(PixelSize))
            {
                return new PixelSize((int)point.X, (int)point.Y);
            }
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }

    public override bool CanConvertFrom(ITypeDescriptorContext? context, [NotNullWhen(true)] Type? sourceType)
    {
        return sourceType == typeof(float[])
            || sourceType == typeof(Tuple<float, float>)
            || sourceType == typeof(Rect)
            || sourceType == typeof(Size)
            || sourceType == typeof(PixelPoint)
            || sourceType == typeof(PixelRect)
            || sourceType == typeof(PixelSize)
            || sourceType == typeof(string);
    }

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is float[] { Length: >= 2 } array)
        {
            return new Point(array[0], array[1]);
        }
        else if (value is Tuple<float, float> tuple)
        {
            return new Point(tuple.Item1, tuple.Item2);
        }
        else if (value is Rect rect)
        {
            return new Point(rect.X, rect.Y);
        }
        else if (value is Size size)
        {
            return new Point(size.Width, size.Height);
        }
        else if (value is PixelPoint pxpoint)
        {
            return new Point(pxpoint.X, pxpoint.Y);
        }
        else if (value is PixelRect pxrect)
        {
            return new Point(pxrect.X, pxrect.Y);
        }
        else if (value is PixelSize pxsize)
        {
            return new Point(pxsize.Width, pxsize.Height);
        }
        else if (value is string str)
        {
            return Point.Parse(str);
        }

        return base.ConvertFrom(context, culture, value);
    }
}
