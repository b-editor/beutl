using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using Beutl.Graphics;

namespace Beutl.Converters;

public sealed class MatrixConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, [NotNullWhen(true)] Type? sourceType)
    {
        return sourceType == typeof(float[])
            || sourceType == typeof(string);
    }

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is float[] array)
        {
            if (array.Length == 6)
            {
                return new Matrix
                (
                    array[0], array[1],
                    array[2], array[3],
                    array[4], array[5]
                );
            }
            else if (array.Length >= 9)
            {
                return new Matrix
                (
                    array[0], array[1], array[2],
                    array[3], array[4], array[5],
                    array[6], array[7], array[8]
                );
            }
        }
        else if (value is string str)
        {
            return Matrix.Parse(str);
        }

        return base.ConvertFrom(context, culture, value);
    }

    public override bool CanConvertTo(ITypeDescriptorContext? context, [NotNullWhen(true)] Type? destinationType)
    {
        return destinationType == typeof(float[])
            || base.CanConvertTo(context, destinationType);
    }

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        if (value is Matrix matrix
            && destinationType == typeof(float[]))
        {
            return new float[]
            {
                matrix.M11, matrix.M12, matrix.M13,
                matrix.M21, matrix.M22, matrix.M23,
                matrix.M31, matrix.M32, matrix.M33,
            };
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }
}
