using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
using Beutl.Pages;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

// Guards that the extensions window adapts to the available space instead of relying on a hard 800x600
// size with a fixed-width search box. The window must declare MinWidth/MinHeight floors, and the search
// box must shrink (and stay inside its slot) on a narrow window rather than overflowing it. Asserts
// arranged layout bounds only (no pixel readback): frame capture of an inflated shell view crashes the
// headless host on software Vulkan.
[TestFixture]
public class ExtensionsPageLayoutTests
{
    private static TextBox GetSearchBox(ExtensionsPage page)
        => page.GetVisualDescendants().OfType<TextBox>().First(x => x.Name == "searchTextBox");

    [AvaloniaTest]
    public void Window_declares_minimum_size_floors()
    {
        var page = new ExtensionsPage();
        try
        {
            Assert.That(page.MinWidth, Is.GreaterThan(0), "window must declare a MinWidth floor so it can shrink without clipping");
            Assert.That(page.MinHeight, Is.GreaterThan(0), "window must declare a MinHeight floor so it can shrink without clipping");
        }
        finally
        {
            page.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public void Search_box_shrinks_and_stays_in_its_slot_on_a_narrow_window()
    {
        var page = new ExtensionsPage();
        // Request a window narrower than the old 800px content width. The MinWidth floor clamps it up to a
        // usable minimum; the responsive search box must then shrink to fit instead of overflowing.
        page.Width = 360;
        page.Height = 600;
        try
        {
            page.Show();
            HeadlessTestHelpers.Render();

            TextBox tb = GetSearchBox(page);
            Assert.That(
                tb.MinWidth,
                Is.LessThan(250),
                "search box must no longer pin itself to a 250px floor so it can shrink at narrow widths");
            Assert.That(
                tb.Bounds.X,
                Is.GreaterThanOrEqualTo(0),
                "search box must stay inside its slot at a narrow width rather than overflowing (clipping) its left edge");
            Assert.That(
                tb.Bounds.Width,
                Is.GreaterThan(0).And.LessThan(250),
                "search box must shrink below its 250px cap when the top bar is narrow");
        }
        finally
        {
            page.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public void Search_box_caps_its_width_on_a_wide_window()
    {
        var page = new ExtensionsPage();
        page.Width = 1200;
        page.Height = 700;
        try
        {
            page.Show();
            HeadlessTestHelpers.Render();

            TextBox tb = GetSearchBox(page);
            Assert.That(
                tb.Bounds.Width,
                Is.GreaterThan(0).And.LessThanOrEqualTo(251),
                "search box must cap at its ~250px MaxWidth on a wide window instead of stretching across the top bar");
        }
        finally
        {
            page.Close();
            HeadlessTestHelpers.Settle();
        }
    }
}
