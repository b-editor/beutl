using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Beutl.Testing.Headless;
using FluentAvalonia.UI.Controls;

namespace Beutl.E2ETests.Controls;

[TestFixture]
public class ColorPaletteItemTests
{
    [AvaloniaTest]
    public void Nonuniform_border_renders_after_geometry_cache_changes()
    {
        var item = new ColorPaletteItem
        {
            Width = 90,
            Height = 90,
            Color = Colors.Red,
            BorderBrush = Brushes.Black,
            BorderBrushPointerOver = null,
            BorderBrushPressed = null,
            BorderThickness = new Thickness(1, 2, 3, 4),
            CornerRadius = new CornerRadius(2, 4, 6, 8),
        };
        using var host = new ControlHost(item, 120, 120);
        HeadlessTestHelpers.Render();
        using var first = host.Window.CaptureRenderedFrame();
        Assert.That(first, Is.Not.Null);

        item.CornerRadius = new CornerRadius(8, 6, 4, 2);
        HeadlessTestHelpers.Render();
        using var second = host.Window.CaptureRenderedFrame();
        Assert.That(second, Is.Not.Null);
    }
}
