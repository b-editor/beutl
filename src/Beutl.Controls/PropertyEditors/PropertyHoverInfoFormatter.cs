using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Beutl.Language;

#nullable enable annotations

namespace Beutl.Controls.PropertyEditors;

public static class PropertyHoverInfoFormatter
{
    public static string? Format(Type? propertyType, string? description, IEnumerable<Attribute>? attributes)
    {
        var lines = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(description)) lines.Add(description.Trim());
        if (propertyType != null)
        {
            lines.Add(string.Format(CultureInfo.CurrentCulture, Strings.PropertyHover_Type,
                FormatTypeName(propertyType)));
        }

        RangeAttribute? range = attributes?.OfType<RangeAttribute>().FirstOrDefault();
        if (range != null)
        {
            lines.Add(string.Format(CultureInfo.CurrentCulture, Strings.PropertyHover_Range,
                FormatBoundary(range.Minimum), FormatBoundary(range.Maximum)));
        }

        return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    private static string FormatTypeName(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
            return $"{FormatTypeName(underlying)}?";
        if (type.IsArray) return $"{FormatTypeName(type.GetElementType()!)}[]";

        if (type == typeof(bool)) return "bool";
        if (type == typeof(byte)) return "byte";
        if (type == typeof(sbyte)) return "sbyte";
        if (type == typeof(short)) return "short";
        if (type == typeof(ushort)) return "ushort";
        if (type == typeof(int)) return "int";
        if (type == typeof(uint)) return "uint";
        if (type == typeof(long)) return "long";
        if (type == typeof(ulong)) return "ulong";
        if (type == typeof(float)) return "float";
        if (type == typeof(double)) return "double";
        if (type == typeof(decimal)) return "decimal";
        if (type == typeof(char)) return "char";
        if (type == typeof(string)) return "string";
        if (type == typeof(object)) return "object";

        string name = TypeDisplayHelpers.GetLocalizedName(type);
        if (!type.IsGenericType) return name;
        int suffix = name.IndexOf('`');
        if (suffix >= 0) name = name[..suffix];
        return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(FormatTypeName))}>";
    }

    private static string FormatBoundary(object? value)
        => value is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.CurrentCulture) ?? string.Empty
            : value?.ToString() ?? string.Empty;
}
