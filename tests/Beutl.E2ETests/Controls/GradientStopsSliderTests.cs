using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Beutl.Controls.PropertyEditors;
using Beutl.Testing.Headless;

namespace Beutl.E2ETests.Controls;

[TestFixture]
public class GradientStopsSliderTests
{
    [AvaloniaTest]
    public void Clicking_between_two_stops_adds_a_stop_colored_between_them()
    {
        var stops = new GradientStops
        {
            new GradientStop(Colors.Black, 0.5),
            new GradientStop(Colors.White, 1.0),
        };
        var slider = new GradientStopsSlider { Stops = stops };
        var added = new List<GradientStop>();
        slider.Added += (_, e) =>
        {
            added.Add(e.Object);
            stops.Insert(e.Index, e.Object);
        };
        var window = new Window { Content = slider, Width = 420, Height = 120 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Settle();
            ItemsControl items = slider.GetVisualDescendants().OfType<ItemsControl>().Single(c => c.Name == "ItemsControl");
            // The slider maps x to x / (width - 18), the track left after a thumb's width.
            double dragWidth = items.Bounds.Width - 18;
            Point click = items.TranslatePoint(new Point(dragWidth * 0.75, items.Bounds.Height / 2), window)!.Value;

            window.MouseDown(click, MouseButton.Left);
            window.MouseUp(click, MouseButton.Left);
            HeadlessTestHelpers.Settle();

            GradientStop stop = added.Single();
            Assert.That(stop.Offset, Is.EqualTo(0.75).Within(1e-6));
            // Halfway between black and white in linear light.
            Assert.That(stop.Color, Is.EqualTo(Color.FromRgb(188, 188, 188)));
        }
        finally
        {
            window.Close();
        }
    }

    // A click there lands on the stops' thumbs, so the color is resolved directly.
    [AvaloniaTest]
    public void A_stop_added_where_two_stops_overlap_takes_the_earlier_stops_color()
    {
        var stops = new GradientStops
        {
            new GradientStop(Colors.Red, 0.5),
            new GradientStop(Colors.Blue, 0.5),
        };
        MethodInfo resolve = typeof(GradientStopsSlider).GetMethod(
            "ResolveInsertedStop", BindingFlags.NonPublic | BindingFlags.Static)!;

        var (color, index) = ((Color, int))resolve.Invoke(null, [stops, 0.5])!;

        Assert.Multiple(() =>
        {
            Assert.That(color, Is.EqualTo(Colors.Red));
            Assert.That(index, Is.EqualTo(1));
        });
    }
}
