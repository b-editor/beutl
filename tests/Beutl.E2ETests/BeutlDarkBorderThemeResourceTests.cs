using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Beutl.Controls.Styling.Themes;

namespace Beutl.E2ETests;

// The PackageTools shell loads no extensions, so it merges the design theme's dictionary itself instead
// of going through ThemeService. This app mirrors that situation — FluentAvalonia plus Beutl.Controls
// styles, without the editor's theme plumbing — so it is where that path is pinned: the dictionary has
// to load standalone (every StaticResource it uses must be a key it defines) and override the palette.
[TestFixture]
public class BeutlDarkBorderThemeResourceTests
{
    [AvaloniaTest]
    public void ResourceDictionary_MergesStandalone_AndOverridesTheDarkPalette()
    {
        var resources = (IResourceProvider)AvaloniaXamlLoader.Load(BeutlDarkBorderTheme.ResourceUri, null)!;
        IList<IResourceProvider> merged = Application.Current!.Resources.MergedDictionaries;
        ThemeVariant previousVariant = Application.Current.RequestedThemeVariant;
        try
        {
            Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
            merged.Add(resources);

            bool found = Application.Current.TryGetResource(
                "TextFillColorPrimary", ThemeVariant.Dark, out object? value);

            Assert.Multiple(() =>
            {
                Assert.That(found, Is.True, "the merged dictionary must supply the palette keys");
                Assert.That(value, Is.EqualTo(Color.FromRgb(0xEA, 0xEB, 0xED)),
                    "the design value, not FluentAvalonia's stock dark");
            });
        }
        finally
        {
            merged.Remove(resources);
            Application.Current.RequestedThemeVariant = previousVariant;
        }
    }

    [AvaloniaTest]
    public void AccentColor_IsTheDesignBlue()
    {
        // Whoever applies this theme seeds FluentAvalonia's accent shades from here, so the value is
        // part of the theme's look rather than an implementation detail.
        Assert.That(BeutlDarkBorderTheme.AccentColor, Is.EqualTo(Color.FromRgb(0x25, 0x63, 0xEB)));
    }
}
