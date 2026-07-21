using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

// The dock panel-splitter divider is a border-theme-only accent: only the "beutl.dark.border"
// theme (the harness Dark variant) paints it. Light and Dark (Classic) must show no divider line.
// Dark (Classic) is not reachable from this harness (see DarkBorderThemeColorTests), so Light
// stands in as the non-border theme; both resolve through the same DockFluent Transparent entries.
[TestFixture]
public class DockSplitterVisibilityTests
{
    [AvaloniaTest]
    public void Divider_is_hidden_outside_the_border_theme()
    {
        var probe = new Border();
        var window = new Window { Content = probe, Width = 200, Height = 120 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(1);

            Assert.Multiple(() =>
            {
                Assert.That(Resolve(probe, "DockSplitterIdleBrush", ThemeVariant.Light).A, Is.EqualTo(0),
                    "the resting divider must be invisible in Light");
                Assert.That(Resolve(probe, "DockSplitterHoverBrush", ThemeVariant.Light).A, Is.EqualTo(0),
                    "the hovered divider must be invisible in Light");

                Assert.That(Resolve(probe, "DockSplitterIdleBrush", ThemeVariant.Dark).A, Is.GreaterThan(0),
                    "the border theme must paint the resting divider");
                Assert.That(Resolve(probe, "DockSplitterHoverBrush", ThemeVariant.Dark).A, Is.GreaterThan(0),
                    "the border theme must paint the hovered divider");
            });
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    private static Color Resolve(Control context, string key, ThemeVariant variant)
    {
        if (!context.TryFindResource(key, variant, out object? value) || value is not ISolidColorBrush brush)
        {
            Assert.Fail($"'{key}' should resolve to a solid color brush under {variant} (got {value?.GetType().Name ?? "nothing"})");
            return default;
        }

        return brush.Color;
    }
}
