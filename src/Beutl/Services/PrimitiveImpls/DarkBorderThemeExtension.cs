using Avalonia.Styling;
using Beutl.Controls.Styling.Themes;
using Beutl.Extensibility;
using Beutl.Language;

namespace Beutl.Services.PrimitiveImpls;

// The default first-party theme: Beutl's near-black flat-panel design. It ships only the color
// overrides (Styling/Themes/BeutlDarkBorder.axaml); ThemeService merges them over the Dark base
// variant and loads this extension itself, ahead of the primitive-extension pass, so the default
// resolves at the first apply. The built-in "Dark (Classic)" theme is the same base variant without
// this override.
[PrimitiveImpl]
public sealed class DarkBorderThemeExtension : ThemeExtension
{
    // The id, resources and accent are shared with the PackageTools shell, which cannot reach this
    // extension: it applies the same theme from settings without loading extensions.
    public const string ThemeId = FirstPartyThemeIds.DarkBorder;

    public static readonly DarkBorderThemeExtension Instance = new();

    public override string Name => "DarkBorderTheme";

    public override string DisplayName => SettingsStrings.Dark;

    public override ThemeDescriptor GetThemeDescriptor() =>
        new(ThemeId, SettingsStrings.Dark, ThemeVariant.Dark, BeutlDarkBorderTheme.ResourceUri,
            AccentColor: BeutlDarkBorderTheme.AccentColor);
}
