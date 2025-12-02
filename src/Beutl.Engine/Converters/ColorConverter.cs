using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Beutl.Animation;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.Converters;

public sealed class ColorConverter : TypeConverter
{
    public override bool CanConvertTo(ITypeDescriptorContext? context, [NotNullWhen(true)] Type? destinationType)
    {
        return destinationType == typeof(float[])
            || destinationType == typeof(byte[])
            || destinationType == typeof(Tuple<float, float, float, float>)
            || destinationType == typeof(Tuple<byte, byte, byte, byte>)
            || destinationType == typeof(int)
            || destinationType == typeof(uint)
            || destinationType == typeof(Brush)
            || destinationType == typeof(SolidColorBrush)
            || destinationType == typeof(SolidColorBrush.Resource)
            || base.CanConvertTo(context, destinationType);
    }

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        if (value is Color color)
        {
            if (destinationType == typeof(float[]))
            {
                return new float[]
                {
                    color.A / 255f, color.R / 255f, color.G / 255f, color.B / 255f,
                };
            }
            else if (destinationType == typeof(byte[]))
            {
                return new byte[]
                {
                    color.A, color.R, color.G, color.B,
                };
            }
            else if (destinationType == typeof(Tuple<float, float, float, float>))
            {
                return new Tuple<float, float, float, float>(color.A / 255f, color.R / 255f, color.G / 255f, color.B / 255f);
            }
            else if (destinationType == typeof(Tuple<byte, byte, byte, byte>))
            {
                return new Tuple<byte, byte, byte, byte>(color.A, color.R, color.G, color.B);
            }
            else if (destinationType == typeof(int))
            {
                return color.ToInt32();
            }
            else if (destinationType == typeof(uint))
            {
                return color.ToUint32();
            }
            else if (destinationType == typeof(Brush)
                || destinationType == typeof(SolidColorBrush))
            {
                return color.ToBrush();
            }
            else if (destinationType == typeof(SolidColorBrush.Resource))
            {
                return color.ToBrushResource();
            }
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }

    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
    {
        return sourceType == typeof(float[])
            || sourceType == typeof(byte[])
            || sourceType == typeof(Tuple<float, float, float, float>)
            || sourceType == typeof(Tuple<byte, byte, byte, byte>)
            || sourceType == typeof(int)
            || sourceType == typeof(uint)
            || sourceType == typeof(SolidColorBrush)
            || sourceType == typeof(SolidColorBrush.Resource)
            || sourceType == typeof(string);
    }

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is float[] { Length: >= 4 } array)
        {
            return Color.FromArgb((byte)(array[0] * 255), (byte)(array[1] * 255), (byte)(array[2] * 255), (byte)(array[3] * 255));
        }
        else if (value is byte[] { Length: >= 4 } array2)
        {
            return Color.FromArgb(array2[0], array2[1], array2[2], array2[3]);
        }
        else if (value is Tuple<float, float, float, float> tuple1)
        {
            return Color.FromArgb((byte)(tuple1.Item1 * 255), (byte)(tuple1.Item2 * 255), (byte)(tuple1.Item3 * 255), (byte)(tuple1.Item4 * 255));
        }
        else if (value is Tuple<byte, byte, byte, byte> tuple2)
        {
            return Color.FromArgb(tuple2.Item1, tuple2.Item2, tuple2.Item3, tuple2.Item4);
        }
        else if (value is int @int)
        {
            return Color.FromInt32(@int);
        }
        else if (value is uint @uint)
        {
            return Color.FromUInt32(@uint);
        }
        else if (value is SolidColorBrush solidColorBrush)
        {
            return solidColorBrush.Color.GetValue(RenderContext.Default);
        }
        else if (value is SolidColorBrush.Resource solidColorBrushResource)
        {
            return solidColorBrushResource.Color;
        }
        else if (value is string str)
        {
            return Color.Parse(str);
        }

        return base.ConvertFrom(context, culture, value);
    }
}
