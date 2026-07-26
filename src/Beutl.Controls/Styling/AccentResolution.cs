using Avalonia.Controls;
using Avalonia.Media;

namespace Beutl.Controls.Styling;

// Shared by the editor and PackageTools shells. FluentAvalonia resolves OS accents itself.
public static class AccentResolution
{
    // TextOnAccentFillColorDisabled pairs with a fixed translucent white fill, not the accent.
    private static readonly (string Key, byte Alpha)[] s_keys =
    [
        ("TextOnAccentFillColorPrimary", 0xFF),
        ("TextOnAccentFillColorSelectedText", 0xFF),
        ("TextOnAccentFillColorSecondary", 0xC5),
    ];

    /// <summary>
    /// Returns <paramref name="accent"/> at full opacity. Older settings may contain alpha values,
    /// but FluentAvalonia's derived shades require an opaque source color.
    /// </summary>
    public static Color? Normalize(Color? accent) =>
        accent is { } value ? Color.FromRgb(value.R, value.G, value.B) : null;

    /// <summary>
    /// Sets text-on-accent tokens for <paramref name="accent"/>, or removes them when it is null.
    /// <paramref name="resources"/> must be application-level so these entries override theme values.
    /// </summary>
    public static void ApplyTextOnAccent(IResourceDictionary resources, Color? accent)
    {
        ArgumentNullException.ThrowIfNull(resources);
        Color? foreground = accent is { } value ? ResolveForegroundOn(value) : null;
        foreach ((string key, byte alpha) in s_keys)
        {
            if (foreground is { } fg)
            {
                resources[key] = Color.FromArgb(alpha, fg.R, fg.G, fg.B);
            }
            else
            {
                resources.Remove(key);
            }
        }
    }

    // Uses the higher WCAG contrast against black or white; callers provide an opaque accent.
    public static Color ResolveForegroundOn(Color accent)
    {
        double luminance = RelativeLuminance(accent);
        return (luminance + 0.05) / 0.05 > 1.05 / (luminance + 0.05) ? Colors.Black : Colors.White;
    }

    private static double RelativeLuminance(Color color) =>
        (0.2126 * Linearize(color.R)) + (0.7152 * Linearize(color.G)) + (0.0722 * Linearize(color.B));

    private static double Linearize(byte channel)
    {
        double value = channel / 255.0;
        return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}
