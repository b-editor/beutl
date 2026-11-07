using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json.Nodes;

namespace Beutl;

/// <summary>
/// A color kept in a <see cref="ColorPalette"/>: an ARGB value and the name the user gave it, if any.
/// </summary>
/// <param name="Argb">The color as <c>0xAARRGGBB</c>.</param>
/// <param name="Name">The name the user gave the color, or <see langword="null"/>.</param>
public readonly record struct PaletteColor(uint Argb, string? Name = null);

/// <summary>
/// A named set of colors that the color pickers offer for reuse.
/// </summary>
/// <remarks>
/// A palette is immutable; each edit returns a new palette.
/// </remarks>
public sealed record ColorPalette
{
    public ColorPalette(string name, ImmutableArray<PaletteColor> colors)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        Colors = colors.IsDefault ? [] : colors;
    }

    public string Name { get; }

    public ImmutableArray<PaletteColor> Colors { get; }

    public bool Equals(ColorPalette? other)
    {
        return other is not null
               && Name == other.Name
               && Colors.SequenceEqual(other.Colors);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Name, Colors.Length);
    }

    public ColorPalette WithName(string name)
    {
        return new ColorPalette(name, Colors);
    }

    public ColorPalette AddColor(PaletteColor color)
    {
        return new ColorPalette(Name, Colors.Add(color));
    }

    public ColorPalette RemoveColor(int index)
    {
        return new ColorPalette(Name, Colors.RemoveAt(index));
    }

    /// <summary>
    /// Replaces the color at an index, as a rename or a change of its value does.
    /// </summary>
    public ColorPalette SetColor(int index, PaletteColor color)
    {
        return new ColorPalette(Name, Colors.SetItem(index, color));
    }

    /// <summary>
    /// Writes the palette as <c>{"Name": ..., "Colors": [{"Color": "#AARRGGBB", "Name": ...}]}</c>.
    /// </summary>
    public JsonObject ToJson()
    {
        var colors = new JsonArray();
        foreach (PaletteColor color in Colors)
        {
            var obj = new JsonObject { ["Color"] = FormatArgb(color.Argb) };
            if (!string.IsNullOrEmpty(color.Name))
            {
                obj["Name"] = color.Name;
            }

            colors.Add(obj);
        }

        return new JsonObject
        {
            ["Name"] = Name,
            ["Colors"] = colors
        };
    }

    public static JsonArray ToJson(IEnumerable<ColorPalette> palettes)
    {
        var array = new JsonArray();
        foreach (ColorPalette palette in palettes)
        {
            array.Add(palette.ToJson());
        }

        return array;
    }

    /// <summary>
    /// Reads palettes that <see cref="ToJson(IEnumerable{ColorPalette})"/> wrote. A color whose value does not
    /// read, or a member of the wrong kind, is left out rather than failing the read, so a hand-edited file
    /// loses only that part.
    /// </summary>
    public static ImmutableArray<ColorPalette> ReadList(JsonNode? node)
    {
        if (node is not JsonArray array)
        {
            return [];
        }

        ImmutableArray<ColorPalette>.Builder palettes = ImmutableArray.CreateBuilder<ColorPalette>();
        foreach (JsonNode? item in array)
        {
            if (item is JsonObject obj)
            {
                obj.TryGetPropertyValueAsJsonValue("Name", out string? name);
                palettes.Add(new ColorPalette(name ?? string.Empty, ReadColors(obj["Colors"])));
            }
        }

        return palettes.ToImmutable();
    }

    /// <summary>
    /// Formats a color as <c>#AARRGGBB</c>.
    /// </summary>
    public static string FormatArgb(uint argb)
    {
        return "#" + argb.ToString("X8", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Reads <c>#AARRGGBB</c>, or <c>#RRGGBB</c> as an opaque color.
    /// </summary>
    public static bool TryParseArgb(string? s, out uint argb)
    {
        argb = 0;
        if (s is not ['#', .. var hex] || hex.Length is not (6 or 8)
            || !uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint value))
        {
            return false;
        }

        argb = hex.Length == 6 ? 0xFF000000 | value : value;
        return true;
    }

    private static ImmutableArray<PaletteColor> ReadColors(JsonNode? node)
    {
        if (node is not JsonArray array)
        {
            return [];
        }

        ImmutableArray<PaletteColor>.Builder colors = ImmutableArray.CreateBuilder<PaletteColor>();
        foreach (JsonNode? item in array)
        {
            if (item is JsonObject obj
                && obj.TryGetPropertyValueAsJsonValue("Color", out string? hex)
                && TryParseArgb(hex, out uint argb))
            {
                obj.TryGetPropertyValueAsJsonValue("Name", out string? name);
                colors.Add(new PaletteColor(argb, string.IsNullOrEmpty(name) ? null : name));
            }
        }

        return colors.ToImmutable();
    }
}
