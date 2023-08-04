using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using Beutl.Graphics;

namespace Beutl.Converters;

public sealed class ThicknessConverter : TypeConverter
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
        if (value is Thickness thickness)
        {
            if (destinationType == typeof(float[]))
            {
                return new float[] { thickness.Left, thickness.Top, thickness.Right, thickness.Bottom };
            }
            else if (destinationType == typeof(Tuple<float, float, float, float>))
            {
                return new Tuple<float, float, float, float>(thickness.Left, thickness.Top, thickness.Right, thickness.Bottom);
            }
            else if (destinationType == typeof(Tuple<float, float>))
            {
                return new Tuple<float, float>(thickness.Left, thickness.Top);
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
                return new Thickness(array[0]);
            }
            else if (array.Length == 2)
            {
                return new Thickness(array[0], array[1]);
            }
            else if (array.Length == 4)
            {
                return new Thickness(array[0], array[1], array[2], array[3]);
            }
        }
        else if (value is float f)
        {
            return new Thickness(f);
        }
        else if (value is Tuple<float, float> t1)
        {
            return new Thickness(t1.Item1, t1.Item2);
        }
        else if (value is Tuple<float, float, float, float> t2)
        {
            return new Thickness(t2.Item1, t2.Item2, t2.Item3, t2.Item4);
        }
        else if (value is string str)
        {
            return Thickness.Parse(str);
        }

        return base.ConvertFrom(context, culture, value);
    }
}
