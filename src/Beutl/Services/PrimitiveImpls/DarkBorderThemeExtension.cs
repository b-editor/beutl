using Avalonia.Media;
using Avalonia.Styling;
using Beutl.Extensibility;
using Beutl.Language;

namespace Beutl.Services.PrimitiveImpls;

// The default first-party theme: Beutl's near-black flat-panel design. It ships only the color
// overrides (Styling/Themes/BeutlDarkBorder.axaml); ThemeService merges them over the Dark base
// variant. The built-in "Dark (Classic)" theme is the same base variant without this override.
[PrimitiveImpl]
public sealed class DarkBorderThemeExtension : ThemeExtension
{
    // Persisted in settings.json (ViewConfig.Theme) and referenced as the ViewConfig default, so it
    // must stay stable. Not one of BuiltinThemeIds' reserved ids.
    public const string ThemeId = "beutl.dark.border";

    private static readonly Uri s_resourceUri =
        new("avares://Beutl.Controls/Styling/Themes/BeutlDarkBorder.axaml");

    // The design accent. BeutlDarkBorder.axaml's accent surfaces reference SystemAccentColor*
    // dynamically; ThemeService seeds those shades from this value unless the user configured a
    // custom accent, so this is the single source of the theme's default blue.
    private static readonly Color s_accentColor = Color.FromRgb(0x25, 0x63, 0xEB);

    public static readonly DarkBorderThemeExtension Instance = new();

    public override string Name => "DarkBorderTheme";

    public override string DisplayName => SettingsStrings.Dark;

    public override ThemeDescriptor GetThemeDescriptor() =>
        new(ThemeId, SettingsStrings.Dark, ThemeVariant.Dark, s_resourceUri, AccentColor: s_accentColor);
}
