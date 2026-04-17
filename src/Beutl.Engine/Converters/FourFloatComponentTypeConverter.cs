using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Beutl.Converters;

public abstract class FourFloatComponentTypeConverter<T> : TypeConverter
    where T : struct
{
    protected abstract (float A, float B, float C, float D) GetFourComponents(T value);
    protected abstract (float A, float B) GetTwoComponents(T value);
    protected abstract T FromUniform(float f);
    protected abstract T FromTwo(float a, float b);
    protected abstract T FromFour(float a, float b, float c, float d);
    protected abstract T Parse(string s);

    public override bool CanConvertTo(ITypeDescriptorContext? context, [NotNullWhen(true)] Type? destinationType)
    {
        return destinationType == typeof(float[])
            || destinationType == typeof(Tuple<float, float, float, float>)
            || destinationType == typeof(Tuple<float, float>)
            || base.CanConvertTo(context, destinationType);
    }

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        if (value is T typed)
        {
            var (a, b, c, d) = GetFourComponents(typed);
            if (destinationType == typeof(float[]))
            {
                return new float[] { a, b, c, d };
            }
            else if (destinationType == typeof(Tuple<float, float, float, float>))
            {
                return new Tuple<float, float, float, float>(a, b, c, d);
            }
            else if (destinationType == typeof(Tuple<float, float>))
            {
                var (x, y) = GetTwoComponents(typed);
                return new Tuple<float, float>(x, y);
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
            return array.Length switch
            {
                1 => FromUniform(array[0]),
                2 => FromTwo(array[0], array[1]),
                4 => FromFour(array[0], array[1], array[2], array[3]),
                _ => base.ConvertFrom(context, culture, value)
            };
        }
        else if (value is float f)
        {
            return FromUniform(f);
        }
        else if (value is Tuple<float, float> t1)
        {
            return FromTwo(t1.Item1, t1.Item2);
        }
        else if (value is Tuple<float, float, float, float> t2)
        {
            return FromFour(t2.Item1, t2.Item2, t2.Item3, t2.Item4);
        }
        else if (value is string str)
        {
            return Parse(str);
        }

        return base.ConvertFrom(context, culture, value);
    }
}
