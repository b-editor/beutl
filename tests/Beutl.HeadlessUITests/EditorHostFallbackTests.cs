using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
using Beutl.Testing.Headless;
using Beutl.Views;

namespace Beutl.HeadlessUITests;

// Guards the responsive width of EditorHostFallback's "recently used" panel. It must cap at its 600px
// MaxWidth on wide windows but shrink below that on narrow / high-DPI ones, rather than holding a fixed
// 600px width that clips. Asserts arranged layout bounds only (no pixel readback): frame capture of an
// inflated shell view crashes the headless host on software Vulkan.
[TestFixture]
public class EditorHostFallbackTests
{
    private static Grid GetRecentItemsPanel(EditorHostFallback view)
    {
        ListBox recentList = view.GetVisualDescendants()
            .OfType<ListBox>()
            .First(x => x.Name == "recentList");

        // The recent-items panel is the Grid that directly hosts the recent-files ListBox.
        return recentList.GetVisualAncestors().OfType<Grid>().First();
    }

    private static double LeftRelativeTo(Visual descendant, Visual ancestor)
    {
        double x = 0;
        for (Visual? current = descendant;
             current is not null && !ReferenceEquals(current, ancestor);
             current = current.GetVisualParent())
        {
            x += current.Bounds.X;
        }

        return x;
    }

    [AvaloniaTest]
    public void Recent_panel_caps_at_600_on_a_wide_window()
    {
        var view = new EditorHostFallback();
        var window = new Window { Content = view, Width = 1200, Height = 800 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            Grid panel = GetRecentItemsPanel(view);
            Assert.That(
                panel.Bounds.Width,
                Is.EqualTo(600).Within(1.0),
                "On a wide window the recent-items panel should cap at its 600px MaxWidth.");

            // The panel must stay left-anchored (consistent with the header / social rows above and
            // below), not float to the centre of a wide window.
            Assert.That(
                LeftRelativeTo(panel, view),
                Is.LessThan(200),
                "On a wide window the recent-items panel should remain left-anchored, not centred.");
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public void Recent_panel_shrinks_below_600_on_a_narrow_window()
    {
        var view = new EditorHostFallback();
        var window = new Window { Content = view, Width = 320, Height = 600 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            Grid panel = GetRecentItemsPanel(view);
            Assert.That(panel.Bounds.Width, Is.GreaterThan(0));
            Assert.That(
                panel.Bounds.Width,
                Is.LessThan(600),
                "On a narrow window the recent-items panel must shrink below 600px instead of clipping.");
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }
}
