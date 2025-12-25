using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;

using Beutl.Media;

namespace Beutl.Converters;

public sealed class GradingColorConverter : TypeConverter
{
    public override bool CanConvertTo(ITypeDescriptorContext? context, [NotNullWhen(true)] Type? destinationType)
    {
        return destinationType == typeof(float[])
            || destinationType == typeof(Tuple<float, float, float>)
            || destinationType == typeof(Vector3)
            || destinationType == typeof(Color)
            || base.CanConvertTo(context, destinationType);
    }

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        if (value is GradingColor color)
        {
            if (destinationType == typeof(float[]))
            {
                return new float[] { color.R, color.G, color.B };
            }
            else if (destinationType == typeof(Tuple<float, float, float>))
            {
                return new Tuple<float, float, float>(color.R, color.G, color.B);
            }
            else if (destinationType == typeof(Vector3))
            {
                return color.ToVector3();
            }
            else if (destinationType == typeof(Color))
            {
                return color.ToColor();
            }
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }

    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
    {
        return sourceType == typeof(float[])
            || sourceType == typeof(Tuple<float, float, float>)
            || sourceType == typeof(Vector3)
            || sourceType == typeof(Color)
            || sourceType == typeof(string);
    }

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is float[] { Length: >= 3 } array)
        {
            return new GradingColor(array[0], array[1], array[2]);
        }
        else if (value is Tuple<float, float, float> tuple)
        {
            return new GradingColor(tuple.Item1, tuple.Item2, tuple.Item3);
        }
        else if (value is Vector3 vector)
        {
            return GradingColor.FromVector3(vector);
        }
        else if (value is Color color)
        {
            return GradingColor.FromColor(color);
        }
        else if (value is string str)
        {
            return GradingColor.Parse(str);
        }

        return base.ConvertFrom(context, culture, value);
    }
}
