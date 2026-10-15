using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Beutl.Converters;

/// <summary>
/// Converts a string to <typeparamref name="T"/> with its <see cref="IParsable{TSelf}"/> implementation.
/// </summary>
public sealed class ParsableTypeConverter<T> : TypeConverter
    where T : IParsable<T>
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, [NotNullWhen(true)] Type? sourceType)
    {
        return sourceType == typeof(string);
    }

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string str)
        {
            return T.Parse(str, null);
        }

        return base.ConvertFrom(context, culture, value);
    }
}
