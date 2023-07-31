using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using Beutl.Media;

namespace Beutl.Converters;

public sealed class CornerRadiusConverter : TypeConverter
{
    public override bool CanConvertTo(ITypeDescriptorContext? context, [NotNullWhen(true)] Type? destinationType)
    {
        return destinationType == typeof(float[])
            || destinationType == typeof(Tuple<float, float, float, float>)
            || destinationType == typeof(Tuple<float, float>)
            || base.CanConvertTo(context, destinationType);
    }

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        if (value is CornerRadius cr)
        {
            if (destinationType == typeof(float[]))
            {
                return new float[] { cr.TopLeft, cr.TopRight, cr.BottomRight, cr.BottomLeft };
            }
            else if (destinationType == typeof(Tuple<float, float, float, float>))
            {
                return new Tuple<float, float, float, float>(cr.TopLeft, cr.TopRight, cr.BottomRight, cr.BottomLeft);
            }
            else if (destinationType == typeof(Tuple<float, float>))
            {
                return new Tuple<float, float>(cr.TopLeft, cr.BottomLeft);
            }
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }

    public override bool CanConvertFrom(ITypeDescriptorContext? context, [NotNullWhen(true)] Type? sourceType)
    {
        return sourceType == typeof(float[])
            || sourceType == typeof(float)
            || sourceType == typeof(Tuple<float, float>)
            || sourceType == typeof(Tuple<float, float, float, float>)
            || sourceType == typeof(string);
    }

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is float[] array)
        {
            if (array.Length == 1)
            {
                return new CornerRadius(array[0]);
            }
            else if (array.Length == 2)
            {
                return new CornerRadius(array[0], array[1]);
            }
            else if (array.Length == 4)
            {
                return new CornerRadius(array[0], array[1], array[2], array[3]);
            }
        }
        else if (value is float f)
        {
            return new CornerRadius(f);
        }
        else if (value is Tuple<float, float> t1)
        {
            return new CornerRadius(t1.Item1, t1.Item2);
        }
        else if (value is Tuple<float, float, float, float> t2)
        {
            return new CornerRadius(t2.Item1, t2.Item2, t2.Item3, t2.Item4);
        }
        else if (value is string str)
        {
            return CornerRadius.Parse(str);
        }

        return base.ConvertFrom(context, culture, value);
    }
}
