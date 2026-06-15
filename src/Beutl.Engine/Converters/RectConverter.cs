using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using Beutl.Graphics;

namespace Beutl.Converters;

public sealed class RectConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, [NotNullWhen(true)] Type? sourceType)
    {
        return sourceType == typeof(string);
    }

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string str)
        {
            return Rect.Parse(str);
        }

        return base.ConvertFrom(context, culture, value);
    }
}
