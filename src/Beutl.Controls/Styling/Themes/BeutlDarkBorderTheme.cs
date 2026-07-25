using Avalonia.Media;

namespace Beutl.Controls.Styling.Themes;

// The resources behind the first-party dark design theme (FirstPartyThemeIds.DarkBorder). Both shells
// need them: the editor applies them through a ThemeExtension, while PackageTools loads no extensions
// and merges the dictionary itself. BeutlDarkBorder.axaml is self-contained (every StaticResource it
// uses is a key it defines), so merging it without the classic dark dictionaries is valid.
public static class BeutlDarkBorderTheme
{
    public static Uri ResourceUri { get; } =
        new("avares://Beutl.Controls/Styling/Themes/BeutlDarkBorder.axaml");

    // The design accent. The dictionary's accent surfaces reference SystemAccentColor* dynamically, so
    // whoever applies the theme seeds those shades from this value unless the user configured a custom
    // accent; this is the single source of the theme's default blue.
    public static Color AccentColor { get; } = Color.FromRgb(0x25, 0x63, 0xEB);
}
