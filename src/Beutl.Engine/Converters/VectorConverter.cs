using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.Converters;

public sealed class VectorConverter : TypeConverter
{
    public override bool CanConvertTo(ITypeDescriptorContext? context, [NotNullWhen(true)] Type? destinationType)
    {
        return destinationType == typeof(float[])
            || destinationType == typeof(Tuple<float, float>)
            || destinationType == typeof(Point)
            || destinationType == typeof(Size)
            || destinationType == typeof(PixelPoint)
            || destinationType == typeof(PixelSize)
            || base.CanConvertTo(context, destinationType);
    }

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        if (value is Vector vector)
        {
            if (destinationType == typeof(float[]))
            {
                return new float[] { vector.X, vector.Y };
            }
            else if (destinationType == typeof(Tuple<float, float>))
            {
                return new Tuple<float, float>(vector.X, vector.Y);
            }
            else if (destinationType == typeof(Point))
            {
                return new Point(vector.X, vector.Y);
            }
            else if (destinationType == typeof(Size))
            {
                return new Size(vector.X, vector.Y);
            }
            else if (destinationType == typeof(PixelPoint))
            {
                return new PixelPoint((int)vector.X, (int)vector.Y);
            }
            else if (destinationType == typeof(PixelSize))
            {
                return new PixelSize((int)vector.X, (int)vector.Y);
            }
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }

    public override bool CanConvertFrom(ITypeDescriptorContext? context, [NotNullWhen(true)] Type? sourceType)
    {
        return sourceType == typeof(float[])
            || sourceType == typeof(Tuple<float, float>)
            || sourceType == typeof(Point)
            || sourceType == typeof(Size)
            || sourceType == typeof(PixelPoint)
            || sourceType == typeof(PixelSize)
            || sourceType == typeof(string);
    }

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is float[] { Length: >= 2 } array)
        {
            return new Vector(array[0], array[1]);
        }
        else if (value is Tuple<float, float> tuple)
        {
            return new Vector(tuple.Item1, tuple.Item2);
        }
        else if (value is Point point)
        {
            return new Vector(point.X, point.Y);
        }
        else if (value is Size size)
        {
            return new Vector(size.Width, size.Height);
        }
        else if (value is PixelPoint pxpoint)
        {
            return new Vector(pxpoint.X, pxpoint.Y);
        }
        else if (value is PixelSize pxsize)
        {
            return new Vector(pxsize.Width, pxsize.Height);
        }
        else if (value is string str)
        {
            return Vector.Parse(str);
        }

        return base.ConvertFrom(context, culture, value);
    }
}
