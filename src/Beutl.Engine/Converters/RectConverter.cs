using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.Converters;

public sealed class RectConverter : TypeConverter
{
    public override bool CanConvertTo(ITypeDescriptorContext? context, [NotNullWhen(true)] Type? destinationType)
    {
        return destinationType == typeof(float[])
            || destinationType == typeof(Tuple<float, float, float, float>)
            || destinationType == typeof(Point)
            || destinationType == typeof(Size)
            || destinationType == typeof(PixelPoint)
            || destinationType == typeof(PixelRect)
            || destinationType == typeof(PixelSize)
            || base.CanConvertTo(context, destinationType);
    }

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        if (value is Rect rect)
        {
            if (destinationType == typeof(float[]))
            {
                return new float[] { rect.X, rect.Y, rect.Width, rect.Height };
            }
            else if (destinationType == typeof(Tuple<float, float, float, float>))
            {
                return new Tuple<float, float, float, float>(rect.X, rect.Y, rect.Width, rect.Width);
            }
            else if (destinationType == typeof(Point))
            {
                return new Point(rect.X, rect.Y);
            }
            else if (destinationType == typeof(Size))
            {
                return new Size(rect.Width, rect.Height);
            }
            else if (destinationType == typeof(PixelPoint))
            {
                return new PixelPoint((int)rect.X, (int)rect.Y);
            }
            else if (destinationType == typeof(PixelRect))
            {
                return new PixelRect((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height);
            }
            else if (destinationType == typeof(PixelSize))
            {
                return new PixelSize((int)rect.Width, (int)rect.Height);
            }
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }

    public override bool CanConvertFrom(ITypeDescriptorContext? context, [NotNullWhen(true)] Type? sourceType)
    {
        return sourceType == typeof(float[])
            || sourceType == typeof(Tuple<float, float, float, float>)
            || sourceType == typeof(Point)
            || sourceType == typeof(Size)
            || sourceType == typeof(PixelPoint)
            || sourceType == typeof(PixelRect)
            || sourceType == typeof(PixelSize)
            || sourceType == typeof(string);
    }

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is float[] { Length: >= 4 } array)
        {
            return new Rect(array[0], array[1], array[2], array[3]);
        }
        else if (value is Tuple<float, float, float, float> tuple)
        {
            return new Rect(tuple.Item1, tuple.Item2, tuple.Item3, tuple.Item4);
        }
        else if (value is Point point)
        {
            return new Rect(point.X, point.Y, 0, 0);
        }
        else if (value is Size size)
        {
            return new Rect(size);
        }
        else if (value is PixelPoint pxpoint)
        {
            return new Rect(pxpoint.X, pxpoint.Y, 0, 0);
        }
        else if (value is PixelRect pxrect)
        {
            return new Rect(pxrect.X, pxrect.Y, pxrect.Width, pxrect.Height);
        }
        else if (value is PixelSize pxsize)
        {
            return new Rect(0, 0, pxsize.Width, pxsize.Height);
        }
        else if (value is string str)
        {
            return Rect.Parse(str);
        }

        return base.ConvertFrom(context, culture, value);
    }
}
