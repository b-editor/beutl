using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.Converters;

public sealed class PixelRectConverter : TypeConverter
{
    public override bool CanConvertTo(ITypeDescriptorContext? context, [NotNullWhen(true)] Type? destinationType)
    {
        return destinationType == typeof(int[])
            || destinationType == typeof(Tuple<int, int, int, int>)
            || destinationType == typeof(Point)
            || destinationType == typeof(Rect)
            || destinationType == typeof(Size)
            || destinationType == typeof(PixelPoint)
            || destinationType == typeof(PixelSize)
            || base.CanConvertTo(context, destinationType);
    }

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        if (value is PixelRect pxrect)
        {
            if (destinationType == typeof(int[]))
            {
                return new int[] { pxrect.X, pxrect.Y, pxrect.Width, pxrect.Height };
            }
            else if (destinationType == typeof(Tuple<int, int, int, int>))
            {
                return new Tuple<int, int, int, int>(pxrect.X, pxrect.Y, pxrect.Width, pxrect.Width);
            }
            else if (destinationType == typeof(Point))
            {
                return new Point(pxrect.X, pxrect.Y);
            }
            else if (destinationType == typeof(Rect))
            {
                return new Rect(pxrect.X, pxrect.Y, pxrect.Width, pxrect.Height);
            }
            else if (destinationType == typeof(Size))
            {
                return new Size(pxrect.Width, pxrect.Height);
            }
            else if (destinationType == typeof(PixelPoint))
            {
                return new PixelPoint(pxrect.X, pxrect.Y);
            }
            else if (destinationType == typeof(PixelSize))
            {
                return new PixelSize(pxrect.Width, pxrect.Height);
            }
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }

    public override bool CanConvertFrom(ITypeDescriptorContext? context, [NotNullWhen(true)] Type? sourceType)
    {
        return sourceType == typeof(float[])
            || sourceType == typeof(Tuple<int, int, int, int>)
            || sourceType == typeof(Point)
            || sourceType == typeof(Rect)
            || sourceType == typeof(Size)
            || sourceType == typeof(PixelPoint)
            || sourceType == typeof(PixelSize)
            || sourceType == typeof(string);
    }

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is int[] { Length: >= 4 } array)
        {
            return new PixelRect(array[0], array[1], array[2], array[3]);
        }
        else if (value is Tuple<int, int, int, int> tuple)
        {
            return new PixelRect(tuple.Item1, tuple.Item2, tuple.Item3, tuple.Item4);
        }
        else if (value is Point point)
        {
            return new PixelRect((int)point.X, (int)point.Y, 0, 0);
        }
        else if (value is Rect rect)
        {
            return new PixelRect((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height);
        }
        else if (value is Size size)
        {
            return new PixelRect(0,0, (int)size.Width, (int)size.Height);
        }
        else if (value is PixelPoint pxpoint)
        {
            return new PixelRect(pxpoint.X, pxpoint.Y, 0, 0);
        }
        else if (value is PixelSize pxsize)
        {
            return new PixelRect(0, 0, pxsize.Width, pxsize.Height);
        }
        else if (value is string str)
        {
            return PixelRect.Parse(str);
        }

        return base.ConvertFrom(context, culture, value);
    }
}
