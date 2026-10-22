using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Beutl.Language;

namespace Beutl.Editor.PropertyEditors;

internal static class PropertyHoverInfoFormatter
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
        if (CSharpTypeKeywords.TryGetCSharpKeyword(type, out string? keyword)) return keyword;

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
