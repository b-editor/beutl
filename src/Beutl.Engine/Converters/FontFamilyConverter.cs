using System.ComponentModel;
using System.Globalization;

using Beutl.Media;

namespace Beutl.Converters;

public sealed class FontFamilyConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
    {
        return sourceType == typeof(string);
    }

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string str)
        {
            return new FontFamily(str);
        }

        return base.ConvertFrom(context, culture, value);
    }
}
