using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Beutl.Controls.Styling.Themes;

namespace Beutl.E2ETests;

// The PackageTools shell merges the design theme's dictionary itself instead of going through
// ThemeService, and this app mirrors that — FluentAvalonia plus Beutl.Controls styles, no editor theme
// plumbing. So the dictionary must load standalone: every StaticResource it uses is a key it defines.
[TestFixture]
public class BeutlDarkBorderThemeResourceTests
{
    private static readonly Uri s_graphEditorResourceUri =
        new("avares://Beutl.Editor.Components/GraphEditorTab/Resources/GraphEditorResources.axaml");

    [AvaloniaTest]
    public void ResourceDictionary_MergesStandalone_AndOverridesTheDarkPalette()
    {
        var resources = (IResourceProvider)AvaloniaXamlLoader.Load(BeutlDarkBorderTheme.ResourceUri, null)!;
        IList<IResourceProvider> merged = Application.Current!.Resources.MergedDictionaries;
        ThemeVariant? previousVariant = Application.Current.RequestedThemeVariant;
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
        // part of the shipped look.
        Assert.That(BeutlDarkBorderTheme.AccentColor, Is.EqualTo(Color.FromRgb(0x25, 0x63, 0xEB)));
    }

    // The theme is a flat value-swap over the classic dark resources, so a key it defines that they do
    // not is an override of nothing: it reads as a real design decision but changes no pixel. This test
    // app loads only the shared Controls styles, so add the GraphEditor component's classic resources
    // while keeping the theme dictionary itself unmerged.
    [AvaloniaTest]
    public void EveryKeyItOverrides_ResolvesWithoutIt()
    {
        var resources = (IResourceDictionary)AvaloniaXamlLoader.Load(BeutlDarkBorderTheme.ResourceUri, null)!;
        var graphEditorResources = (IResourceProvider)AvaloniaXamlLoader.Load(s_graphEditorResourceUri, null)!;
        IList<IResourceProvider> merged = Application.Current!.Resources.MergedDictionaries;
        try
        {
            merged.Add(graphEditorResources);
            object[] dangling = resources.Keys
                .Where(key => !Application.Current.TryGetResource(key, ThemeVariant.Dark, out _))
                .ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(resources.Keys, Is.Not.Empty, "precondition: the theme defines keys");
                Assert.That(dangling, Is.Empty,
                    "the border theme must override an existing key, never introduce a dangling one");
            });
        }
        finally
        {
            merged.Remove(graphEditorResources);
        }
    }
}
