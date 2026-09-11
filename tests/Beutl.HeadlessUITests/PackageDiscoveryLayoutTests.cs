using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.NUnit;
using Beutl.Controls;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class PackageDiscoveryLayoutTests
{
    [AvaloniaTest]
    public void Package_grid_realizes_and_wraps_items_with_the_current_items_repeater()
    {
        // The discovery pages use the layout included in Beutl.Controls.
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

    [AvaloniaTest]
    public void Resizing_the_window_reflows_items_into_one_column()
    {
        var layout = new HorizontalGridLayout { MinItemWidth = 345, ColumnSpacing = 16, RowSpacing = 16 };
        ItemsRepeater repeater = CreateRepeater(layout);
        var window = new Window { Content = repeater, Width = 740, Height = 300 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Assert.That(repeater.TryGetElement(1)!.Bounds.Y, Is.EqualTo(repeater.TryGetElement(0)!.Bounds.Y));

            window.Width = 400;
            HeadlessTestHelpers.Render();
            Control first = repeater.TryGetElement(0)!;
            Control second = repeater.TryGetElement(1)!;
            Assert.Multiple(() =>
            {
                Assert.That(second.Bounds.X, Is.EqualTo(first.Bounds.X));
                Assert.That(second.Bounds.Y, Is.EqualTo(first.Bounds.Bottom + 16));
                Assert.That(first.Bounds.Width, Is.EqualTo(repeater.Bounds.Width));
            });
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public void Changing_column_limit_and_spacing_updates_existing_items()
    {
        var layout = new HorizontalGridLayout { MinItemWidth = 345, ColumnSpacing = 16, RowSpacing = 16 };
        ItemsRepeater repeater = CreateRepeater(layout);
        var window = new Window { Content = repeater, Width = 740, Height = 300 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            layout.MaxColumns = 1;
            HeadlessTestHelpers.Render();
            Assert.That(repeater.TryGetElement(1)!.Bounds.Y, Is.GreaterThan(repeater.TryGetElement(0)!.Bounds.Bottom));

            layout.MaxColumns = 2;
            layout.ColumnSpacing = 32;
            layout.RowSpacing = 24;
            HeadlessTestHelpers.Render();
            Control first = repeater.TryGetElement(0)!;
            Control second = repeater.TryGetElement(1)!;
            Control third = repeater.TryGetElement(2)!;
            Assert.Multiple(() =>
            {
                Assert.That(second.Bounds.X, Is.EqualTo(first.Bounds.Right + 32));
                Assert.That(second.Bounds.Y, Is.EqualTo(first.Bounds.Y));
                Assert.That(third.Bounds.Y, Is.EqualTo(first.Bounds.Bottom + 24));
            });
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public void Removing_all_items_clears_the_measured_height()
    {
        var layout = new HorizontalGridLayout { MinItemWidth = 345, ColumnSpacing = 16, RowSpacing = 16 };
        ItemsRepeater repeater = CreateRepeater(layout);
        var window = new Window { Content = repeater, Width = 740, Height = 300 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Assert.That(repeater.DesiredSize.Height, Is.GreaterThan(0));

            repeater.ItemsSource = Array.Empty<string>();
            HeadlessTestHelpers.Render();
            Assert.Multiple(() =>
            {
                Assert.That(repeater.TryGetElement(0), Is.Null);
                Assert.That(repeater.DesiredSize.Height, Is.Zero);
            });
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    private static ItemsRepeater CreateRepeater(HorizontalGridLayout layout) => new()
    {
        ItemsSource = new[] { "one", "two", "three", "four" },
        ItemTemplate = new FuncDataTemplate<string>((text, _) => new TextBlock { Text = text, Height = 40 }),
        Layout = layout,
    };
}
