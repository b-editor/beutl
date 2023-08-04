using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.Converters;

public sealed class PixelSizeConverter : TypeConverter
{
    public override bool CanConvertTo(ITypeDescriptorContext? context, [NotNullWhen(true)] Type? destinationType)
    {
        return destinationType == typeof(int[])
            || destinationType == typeof(Tuple<int, int>)
            || destinationType == typeof(Point)
            || destinationType == typeof(Rect)
            || destinationType == typeof(Vector)
            || destinationType == typeof(PixelPoint)
            || destinationType == typeof(PixelRect)
            || destinationType == typeof(Size)
            || base.CanConvertTo(context, destinationType);
    }

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        if (value is PixelSize pxsize)
        {
            if (destinationType == typeof(int[]))
            {
                return new int[] { pxsize.Width, pxsize.Height };
            }
            else if (destinationType == typeof(Tuple<int, int>))
            {
                return new Tuple<int, int>(pxsize.Width, pxsize.Width);
            }
            else if (destinationType == typeof(Point))
            {
                return new Point(pxsize.Width, pxsize.Height);
            }
            else if (destinationType == typeof(Rect))
            {
                return new Rect(0, 0, pxsize.Width, pxsize.Height);
            }
            else if (destinationType == typeof(Size))
            {
                return new Size(pxsize.Width, pxsize.Height);
            }
            else if (destinationType == typeof(Vector))
            {
                return new Vector(pxsize.Width, pxsize.Height);
            }
            else if (destinationType == typeof(PixelPoint))
            {
                return new PixelPoint(pxsize.Width, pxsize.Height);
            }
            else if (destinationType == typeof(PixelRect))
            {
                return new PixelRect(0, 0, pxsize.Width, pxsize.Height);
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
            || sourceType == typeof(Vector)
            || sourceType == typeof(PixelPoint)
            || sourceType == typeof(PixelRect)
            || sourceType == typeof(string);
    }

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is int[] { Length: >= 2 } array)
        {
            return new PixelSize(array[0], array[1]);
        }
        else if (value is Tuple<int, int> tuple)
        {
            return new PixelSize(tuple.Item1, tuple.Item2);
        }
        else if (value is Point point)
        {
            return new PixelSize((int)point.X, (int)point.Y);
        }
        else if (value is Rect rect)
        {
            return new PixelSize((int)rect.Width, (int)rect.Height);
        }
        else if (value is Size pxsize)
        {
            return new PixelSize((int)pxsize.Width, (int)pxsize.Height);
        }
        else if (value is Vector vector)
        {
            return new PixelSize((int)vector.X, (int)vector.Y);
        }
        else if (value is PixelPoint pxpoint)
        {
            return new PixelSize(pxpoint.X, pxpoint.Y);
        }
        else if (value is PixelRect pxrect)
        {
            return new PixelSize(pxrect.X, pxrect.Y);
        }
        else if (value is string str)
        {
            return PixelSize.Parse(str);
        }

        return base.ConvertFrom(context, culture, value);
    }
}
