using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.Converters;

public sealed class SizeConverter : TypeConverter
{
    public override bool CanConvertTo(ITypeDescriptorContext? context, [NotNullWhen(true)] Type? destinationType)
    {
        return destinationType == typeof(float[])
            || destinationType == typeof(Tuple<float, float>)
            || destinationType == typeof(Point)
            || destinationType == typeof(Rect)
            || destinationType == typeof(Vector)
            || destinationType == typeof(PixelPoint)
            || destinationType == typeof(PixelRect)
            || destinationType == typeof(PixelSize)
            || base.CanConvertTo(context, destinationType);
    }

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        if (value is Size size)
        {
            if (destinationType == typeof(float[]))
            {
                return new float[] { size.Width, size.Height };
            }
            else if (destinationType == typeof(Tuple<float, float>))
            {
                return new Tuple<float, float>(size.Width, size.Width);
            }
            else if (destinationType == typeof(Point))
            {
                return new Point(size.Width, size.Height);
            }
            else if (destinationType == typeof(Rect))
            {
                return new Rect(size);
            }
            else if (destinationType == typeof(Vector))
            {
                return new Vector(size.Width, size.Height);
            }
            else if (destinationType == typeof(PixelPoint))
            {
                return new PixelPoint((int)size.Width, (int)size.Height);
            }
            else if (destinationType == typeof(PixelRect))
            {
                return new PixelRect(0, 0, (int)size.Width, (int)size.Height);
            }
            else if (destinationType == typeof(PixelSize))
            {
                return new PixelSize((int)size.Width, (int)size.Height);
            }
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }

    public override bool CanConvertFrom(ITypeDescriptorContext? context, [NotNullWhen(true)] Type? sourceType)
    {
        return sourceType == typeof(float[])
            || sourceType == typeof(Tuple<float, float>)
            || sourceType == typeof(Point)
            || sourceType == typeof(Rect)
            || sourceType == typeof(Vector)
            || sourceType == typeof(PixelPoint)
            || sourceType == typeof(PixelRect)
            || sourceType == typeof(PixelSize)
            || sourceType == typeof(string);
    }

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is float[] { Length: >= 2 } array)
        {
            return new Size(array[0], array[1]);
        }
        else if (value is Tuple<float, float> tuple)
        {
            return new Size(tuple.Item1, tuple.Item2);
        }
        else if (value is Point point)
        {
            return new Size(point.X, point.Y);
        }
        else if (value is Rect rect)
        {
            return new Size(rect.Width, rect.Height);
        }
        else if (value is Vector vector)
        {
            return new Size(vector.X, vector.Y);
        }
        else if (value is PixelPoint pxpoint)
        {
            return new Size(pxpoint.X, pxpoint.Y);
        }
        else if (value is PixelRect pxrect)
        {
            return new Size(pxrect.X, pxrect.Y);
        }
        else if (value is PixelSize pxsize)
        {
            return new Size(pxsize.Width, pxsize.Height);
        }
        else if (value is string str)
        {
            return Size.Parse(str);
        }

        return base.ConvertFrom(context, culture, value);
    }
}
