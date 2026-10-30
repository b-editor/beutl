using System.Globalization;

namespace Beutl.Configuration;

/// <summary>A frame shape, such as 9:16, that the preview outlines inside the scene's frame.</summary>
public readonly record struct PreviewGuideAspectRatio
{
    public PreviewGuideAspectRatio(double width, double height)
    {
        if (!IsSide(width)) throw new ArgumentOutOfRangeException(nameof(width));
        if (!IsSide(height)) throw new ArgumentOutOfRangeException(nameof(height));

        Width = width;
        Height = height;
    }

    public double Width { get; }

    public double Height { get; }

    public double Value => Width / Height;

    public override string ToString()
        => $"{Width.ToString(CultureInfo.InvariantCulture)}:{Height.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Reads <c>W:H</c>, such as <c>2.39:1</c>, or a lone <c>W</c> meaning <c>W:1</c>.</summary>
    /// <remarks>Numbers are read in the invariant culture: a comma separates ratios in a list.</remarks>
    public static bool TryParse(ReadOnlySpan<char> text, out PreviewGuideAspectRatio ratio)
    {
        text = text.Trim();
        int colon = text.IndexOf(':');
        ReadOnlySpan<char> width = colon < 0 ? text : text[..colon];
        ReadOnlySpan<char> height = colon < 0 ? "1" : text[(colon + 1)..];
        if (TryParseSide(width, out double w) && TryParseSide(height, out double h) && double.IsFinite(w / h))
        {
            ratio = new PreviewGuideAspectRatio(w, h);
            return true;
        }

        ratio = default;
        return false;
    }

    /// <summary>
    /// Reads a comma-separated list, such as <c>9:16, 1:1</c>. False when an entry is not a ratio, which
    /// <paramref name="ratios"/> then leaves out; an empty list is valid.
    /// </summary>
    public static bool TryParseList(string? text, out PreviewGuideAspectRatio[] ratios)
    {
        var list = new List<PreviewGuideAspectRatio>();
        bool valid = true;
        foreach (string entry in (text ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (TryParse(entry, out PreviewGuideAspectRatio ratio))
                list.Add(ratio);
            else
                valid = false;
        }

        ratios = [.. list];
        return valid;
    }

    private static bool TryParseSide(ReadOnlySpan<char> text, out double value)
        => double.TryParse(text.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)
           && IsSide(value);

    private static bool IsSide(double value) => value > 0 && double.IsFinite(value);
}
