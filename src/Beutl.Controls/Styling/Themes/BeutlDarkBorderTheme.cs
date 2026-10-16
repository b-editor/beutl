using Avalonia.Media;

namespace Beutl.Controls.Styling.Themes;

// PackageTools merges this dictionary directly instead of through a ThemeExtension, so
// BeutlDarkBorder.axaml must define every key its StaticResources use.
internal static class BeutlDarkBorderTheme
{
    public static Uri ResourceUri { get; } =
        new("avares://Beutl.Controls/Styling/Themes/BeutlDarkBorder.axaml");

    // The dictionary's accent surfaces reference SystemAccentColor* dynamically, so whoever applies the
    // theme seeds those shades from here unless the user configured a custom accent.
    public static Color AccentColor { get; } = Color.FromRgb(0x25, 0x63, 0xEB);
}
