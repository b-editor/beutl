using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.Converters;

public sealed class PixelPointConverter : TypeConverter
{
    public override bool CanConvertTo(ITypeDescriptorContext? context, [NotNullWhen(true)] Type? destinationType)
    {
        return destinationType == typeof(int[])
            || destinationType == typeof(Tuple<int, int>)
            || destinationType == typeof(Point)
            || destinationType == typeof(Rect)
            || destinationType == typeof(Size)
            || destinationType == typeof(PixelRect)
            || destinationType == typeof(PixelSize)
            || base.CanConvertTo(context, destinationType);
    }

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        if (value is PixelPoint pxpoint)
        {
            if (destinationType == typeof(int[]))
            {
                return new int[] { pxpoint.X, pxpoint.Y };
            }
            else if (destinationType == typeof(Tuple<int, int>))
            {
                return new Tuple<int, int>(pxpoint.X, pxpoint.Y);
            }
            else if (destinationType == typeof(Point))
            {
                return new Point(pxpoint.X, pxpoint.Y);
            }
            else if (destinationType == typeof(Rect))
            {
                return new Rect(pxpoint.X, pxpoint.Y, 0, 0);
            }
            else if (destinationType == typeof(Size))
            {
                return new Size(pxpoint.X, pxpoint.Y);
            }
            else if (destinationType == typeof(PixelRect))
            {
                return new PixelRect(pxpoint.X, pxpoint.Y, 0, 0);
            }
            else if (destinationType == typeof(PixelSize))
            {
                return new PixelSize(pxpoint.X, pxpoint.Y);
            }
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }

    public override bool CanConvertFrom(ITypeDescriptorContext? context, [NotNullWhen(true)] Type? sourceType)
    {
        return sourceType == typeof(int[])
            || sourceType == typeof(Tuple<int, int>)
            || sourceType == typeof(Point)
            || sourceType == typeof(Rect)
            || sourceType == typeof(Size)
            || sourceType == typeof(PixelRect)
            || sourceType == typeof(PixelSize)
            || sourceType == typeof(string);
    }

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is int[] { Length: >= 2 } array)
        {
            return new PixelPoint(array[0], array[1]);
        }
        else if (value is Tuple<int, int> tuple)
        {
            return new PixelPoint(tuple.Item1, tuple.Item2);
        }
        else if (value is Point point)
        {
            return new PixelPoint((int)point.X, (int)point.Y);
        }
        else if (value is Rect rect)
        {
            return new PixelPoint((int)rect.X, (int)rect.Y);
        }
        else if (value is Size size)
        {
            return new PixelPoint((int)size.Width, (int)size.Height);
        }
        else if (value is PixelRect pxrect)
        {
            return new PixelPoint(pxrect.X, pxrect.Y);
        }
        else if (value is PixelSize pxsize)
        {
            return new PixelPoint(pxsize.Width, pxsize.Height);
        }
        else if (value is string str)
        {
            return PixelPoint.Parse(str);
        }

        return base.ConvertFrom(context, culture, value);
    }
}
