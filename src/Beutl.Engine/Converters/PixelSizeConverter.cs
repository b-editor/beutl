using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using Beutl.Media;

namespace Beutl.Converters;

public sealed class PixelSizeConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, [NotNullWhen(true)] Type? sourceType)
    {
        return sourceType == typeof(string);
    }

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string str)
        {
            return PixelSize.Parse(str);
        }

        return base.ConvertFrom(context, culture, value);
    }
}
