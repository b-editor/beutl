using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Editor.Components.GraphEditorTab.Views;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

// GraphEditor keeps its classic Light/Dark resources in the component dictionary. ThemeExtension
// resources are merged flat at Application scope, so they must both replace those resources on a
// live view and supply the first-party Dark design palette.
[TestFixture]
public class GraphEditorThemeResourceTests
{
    [AvaloniaTest]
    public void DarkTheme_UsesTheTimelinePalette()
    {
        ThemeVariant? previousVariant = Application.Current!.RequestedThemeVariant;
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        Uri resourceUri = DarkBorderThemeExtension.Instance.GetThemeDescriptor().ResourceUri!;
        var resources = (IResourceProvider)AvaloniaXamlLoader.Load(resourceUri, null)!;
        IList<IResourceProvider> merged = Application.Current.Resources.MergedDictionaries;
        var probe = new Border();
        var window = new Window { Content = probe, Width = 200, Height = 120 };
        try
        {
            merged.Add(resources);
            window.Show();
            HeadlessTestHelpers.Render(1);

            Assert.Multiple(() =>
            {
                Assert.That(ResolveFill(probe, "GraphEditorBackgroundBrush"),
                    Is.EqualTo(ResolveFill(probe, "DockSurfacePanelBrush")));
                Assert.That(ResolveFill(probe, "GraphEditorGridLineBrush"),
                    Is.EqualTo(ResolveFill(probe, "TextFillColorTertiaryBrush")));
                Assert.That(ResolveFill(probe, "GraphEditorScaleTextBrush"),
                    Is.EqualTo(ResolveFill(probe, "TextControlForeground")));
                Assert.That(ResolveFill(probe, "GraphEditorControlPointFillBrush"),
                    Is.EqualTo(ResolveFill(probe, "SystemFillColorCautionBrush")));
                Assert.That(ResolveFill(probe, "GraphEditorControlPointStrokeBrush"),
                    Is.EqualTo(ResolveFill(probe, "SystemFillColorCautionBrush")));
                Assert.That(ResolveFill(probe, "GraphEditorHandleLineBrush"),
                    Is.EqualTo(ResolveFill(probe, "TextControlForeground")));
                Assert.That(ResolveFill(probe, "GraphEditorKeyFrameBrush"),
                    Is.EqualTo(ResolveFill(probe, "SystemFillColorCautionBrush")));
            });
        }
        finally
        {
            window.Close();
            merged.Remove(resources);
            Application.Current.RequestedThemeVariant = previousVariant;
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public void ThemeExtension_CanOverrideGraphEditorColors()
    {
        var overrideBrush = new SolidColorBrush(Colors.Magenta);
        var resources = new ResourceDictionary
        {
            ["GraphEditorBackgroundBrush"] = overrideBrush
        };
        IList<IResourceProvider> merged = Application.Current!.Resources.MergedDictionaries;
        ThemeVariant? previousVariant = Application.Current.RequestedThemeVariant;
        var view = new GraphEditorView();
        var window = new Window { Content = view, Width = 200, Height = 120 };
        try
        {
            Application.Current.RequestedThemeVariant = ThemeVariant.Light;
            window.Show();
            HeadlessTestHelpers.Render(1);

            Grid grid = view.GetVisualDescendants().OfType<Grid>().Single(item => item.Name == "grid");
            Assert.That(grid.Background, Is.Not.SameAs(overrideBrush), "precondition: the base theme is active");

            merged.Add(resources);
            HeadlessTestHelpers.Render(1);

            Assert.That(grid.Background, Is.SameAs(overrideBrush));

            merged.Remove(resources);
            HeadlessTestHelpers.Render(1);

            Assert.That(grid.Background, Is.Not.SameAs(overrideBrush),
                "removing the extension resources should restore the base theme");
        }
        finally
        {
            window.Close();
            merged.Remove(resources);
            Application.Current.RequestedThemeVariant = previousVariant;
            HeadlessTestHelpers.Settle();
        }
    }

    private static (Color Color, double Opacity) ResolveFill(Control context, string key)
    {
        if (!context.TryFindResource(key, ThemeVariant.Dark, out object? value)
            || value is not ISolidColorBrush brush)
        {
            Assert.Fail($"'{key}' should resolve to a solid color brush under Dark "
                + $"(got {value?.GetType().Name ?? "nothing"})");
            return default;
        }

        return (brush.Color, brush.Opacity);
    }
}
