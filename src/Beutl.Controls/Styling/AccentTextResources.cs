using Avalonia.Controls;
using Avalonia.Media;

namespace Beutl.Controls.Styling;

// A theme authors its text-on-accent tokens for the accent it declares, but the accent Beutl resolves
// — a theme's design accent, or the user's custom one — can be any color, and white text on a
// near-white fill is unreadable. Shared, because both shells resolve an accent: the editor's
// ThemeService and the PackageTools App, which applies the same theme without the editor's theme
// plumbing. An OS accent is left alone; FluentAvalonia resolves that color.
public static class AccentTextResources
{
    // Alpha comes from here rather than from the theme's own value, so the derived set is one scheme
    // regardless of which theme authored it.
    //
    // TextOnAccentFillColorDisabled is deliberately absent. Despite the name it is never drawn on the
    // accent: every consumer pairs it with AccentFillColorDisabled, a fixed translucent white that does
    // not follow the accent, so deriving it would put a light accent's black glyph on a near-black
    // disabled fill. It stays whatever the theme authored.
    private static readonly (string Key, byte Alpha)[] s_keys =
    [
        ("TextOnAccentFillColorPrimary", 0xFF),
        ("TextOnAccentFillColorSelectedText", 0xFF),
        ("TextOnAccentFillColorSecondary", 0xC5),
    ];

    /// <summary>
    /// Overrides the text-on-accent color tokens that are actually drawn on the accent for
    /// <paramref name="accent"/>, or removes them when it is null so the theme's own values apply
    /// again. Pass an application-level dictionary:
    /// entries there win over the merged theme dictionaries, whose brushes take these colors by
    /// DynamicResource.
    /// </summary>
    public static void Apply(IResourceDictionary resources, Color? accent)
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

    // WCAG contrast against white and against black, whichever is higher.
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
