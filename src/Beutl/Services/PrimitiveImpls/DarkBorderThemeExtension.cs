using Avalonia.Styling;
using Beutl.Controls.Styling.Themes;
using Beutl.Extensibility;
using Beutl.Language;

namespace Beutl.Services.PrimitiveImpls;

// The default first-party theme: Beutl's near-black flat-panel design. It ships only the color
// overrides, which ThemeService merges over the Dark base variant; the built-in "Dark (Classic)"
// theme is that same base variant without them.
[PrimitiveImpl]
public sealed class DarkBorderThemeExtension : ThemeExtension
{
    // Shared with the PackageTools shell, which applies the same theme from settings but cannot reach
    // this extension: it loads none.
    public const string ThemeId = FirstPartyThemeIds.DarkBorder;

    public static readonly DarkBorderThemeExtension Instance = new();

    // One instance for the life of the extension. ThemeRegistry keys ownership on the descriptor
    // instance and ThemeService skips a re-apply by reference, so a fresh record per call would turn a
    // repeat Load into a full revert/apply cycle — and, if two threads Load concurrently, leave
    // Descriptor naming the instance that lost the registry write, which Unload could then not remove.
    private static readonly ThemeDescriptor s_descriptor =
        new(ThemeId, SettingsStrings.Dark, ThemeVariant.Dark, BeutlDarkBorderTheme.ResourceUri,
            AccentColor: BeutlDarkBorderTheme.AccentColor);

    public override string Name => "DarkBorderTheme";

    public override string DisplayName => SettingsStrings.Dark;

    public override ThemeDescriptor GetThemeDescriptor() => s_descriptor;
}
