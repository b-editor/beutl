using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.NUnit;
using Beutl.Testing.Headless;
using PanelExtension;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class PackageDiscoveryLayoutTests
{
    [AvaloniaTest]
    public void Package_grid_realizes_and_wraps_items_with_the_current_items_repeater()
    {
        // The discovery pages use PanelExtension, which is distributed separately from Avalonia.
        var repeater = new ItemsRepeater
        {
            ItemsSource = new[] { "one", "two", "three", "four" },
            ItemTemplate = new FuncDataTemplate<string>((text, _) => new TextBlock { Text = text, Height = 40 }),
            Layout = new HorizontalGridLayout
            {
                MinItemWidth = 345,
                ColumnSpacing = 16,
                RowSpacing = 16,
            },
        };
        var window = new Window { Content = repeater, Width = 740, Height = 300 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            Control first = repeater.TryGetElement(0)!;
            Control second = repeater.TryGetElement(1)!;
            Control third = repeater.TryGetElement(2)!;
            Assert.Multiple(() =>
            {
                Assert.That(repeater.TryGetElement(3), Is.Not.Null);
                Assert.That(first.Bounds.Width, Is.GreaterThanOrEqualTo(345));
                Assert.That(second.Bounds.X, Is.GreaterThan(first.Bounds.Right));
                Assert.That(second.Bounds.Y, Is.EqualTo(first.Bounds.Y));
                Assert.That(third.Bounds.Y, Is.GreaterThan(first.Bounds.Bottom));
            });
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }
}
