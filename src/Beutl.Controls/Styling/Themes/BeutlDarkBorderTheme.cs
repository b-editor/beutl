using Avalonia.Media;

namespace Beutl.Controls.Styling.Themes;

// The resources behind the first-party dark design theme (FirstPartyThemeIds.DarkBorder). Shared,
// because PackageTools loads no extensions and merges the dictionary itself instead of going through
// the editor's ThemeExtension. BeutlDarkBorder.axaml must stay self-contained (every StaticResource it
// uses is a key it defines) for that standalone merge to work.
public static class BeutlDarkBorderTheme
{
    public static Uri ResourceUri { get; } =
        new("avares://Beutl.Controls/Styling/Themes/BeutlDarkBorder.axaml");

    // The dictionary's accent surfaces reference SystemAccentColor* dynamically, so whoever applies the
    // theme seeds those shades from here unless the user configured a custom accent.
    public static Color AccentColor { get; } = Color.FromRgb(0x25, 0x63, 0xEB);
}
