using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Extensibility;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dock;
using Beutl.Views;
using Beutl.Views.Dock;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class DockTabUnderlineTests
{
    [AvaloniaTest]
    public async Task The_selected_tab_keeps_a_whole_underline_after_a_new_tab_is_opened_and_closed()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("dock-tab-underline-new-tab");

        var view = new EditView { DataContext = editor };
        var window = new Window { Content = view, Width = 1200, Height = 700, RequestedThemeVariant = ThemeVariant.Dark };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            IToolDock dock = editor.DockHost.Factory.GetAnchoredDock(DockAnchor.Right)!;
            ToolControl control = ControlFor(view, dock);
            AssertUnderline(window, control, "initially");

            Click(window, control.GetVisualDescendants().OfType<ToolTabAddButton>().Single());
            Assert.That(dock.ActiveDockable, Is.InstanceOf<NewToolTabDockable>());
            AssertUnderline(window, control, "with a new tab open");

            ToolTabStripItem newTab = TabFor(control, dock.ActiveDockable!);
            window.MouseMove(HeadlessTestHelpers.Center(newTab, window));
            HeadlessTestHelpers.Settle();
            Click(window, newTab.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_TabCloseButton"));
            Assert.That(dock.VisibleDockables!.OfType<NewToolTabDockable>(), Is.Empty);
            HeadlessCapture.SaveIfRequested(window, nameof(DockTabUnderlineTests), "after-closing-a-new-tab");
            AssertUnderline(window, control, "after closing the new tab");

            foreach (IDockable dockable in dock.VisibleDockables!.ToArray())
            {
                Click(window, TabFor(control, dockable));
                AssertUnderline(window, control, $"after selecting {dockable.Title}");
            }

            // A hovered tab paints its background, which has to stop short of the baseline under it.
            ToolTabStripItem hovered = TabFor(control, dock.VisibleDockables!.First(d => !ReferenceEquals(d, dock.ActiveDockable)));
            window.MouseMove(HeadlessTestHelpers.Center(hovered, window));
            HeadlessTestHelpers.Settle();
            HeadlessTestHelpers.Render();
            Assert.That(hovered.IsPointerOver, Is.True);
            AssertUnderline(window, control, "while another tab is hovered");
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task The_underline_follows_the_selection_after_the_strip_rejoins_the_tree()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("dock-tab-underline-rejoin");

        var view = new EditView { DataContext = editor };
        var window = new Window { Content = view, Width = 1200, Height = 700, RequestedThemeVariant = ThemeVariant.Dark };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            IToolDock dock = editor.DockHost.Factory.GetAnchoredDock(DockAnchor.Right)!;
            ToolControl control = ControlFor(view, dock);
            ToolTabStrip strip = control.GetVisualDescendants().OfType<ToolTabStrip>().Single();
            var header = (Panel)strip.GetVisualParent()!;
            int index = header.Children.IndexOf(strip);

            // Dock's ToolTabStrip drops its layout subscriptions when it leaves the visual tree and does not
            // take them back when it returns, so whatever it positions from them stops following the tabs.
            // The local DataContext keeps the items, so only those subscriptions are lost.
            strip.DataContext = dock;
            header.Children.RemoveAt(index);
            HeadlessTestHelpers.Render();
            header.Children.Insert(index, strip);
            HeadlessTestHelpers.Render();

            foreach (IDockable dockable in dock.VisibleDockables!.Reverse().ToArray())
            {
                dock.ActiveDockable = dockable;
                HeadlessTestHelpers.Render();
                AssertUnderline(window, control, $"after selecting {dockable.Title}");
            }
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    private static ToolControl ControlFor(Visual root, IToolDock dock)
    {
        return root.GetVisualDescendants()
            .OfType<ToolControl>()
            .Single(control => ReferenceEquals(control.DataContext, dock));
    }

    private static ToolTabStripItem TabFor(Visual root, IDockable dockable)
    {
        return root.GetVisualDescendants()
            .OfType<ToolTabStripItem>()
            .Single(item => ReferenceEquals(item.DataContext, dockable));
    }

    private static void Click(Window window, Control control)
    {
        Point center = HeadlessTestHelpers.Center(control, window);
        window.MouseMove(center);
        HeadlessTestHelpers.Settle();
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        HeadlessTestHelpers.Settle();
        HeadlessTestHelpers.Render();
    }

    // Along the strip's bottom edge, the selected tab's accent underline is two pixels tall across the
    // tab's whole width, and the one-pixel baseline runs everywhere else.
    private static void AssertUnderline(Window window, ToolControl control, string when)
    {
        ToolTabStrip strip = control.GetVisualDescendants().OfType<ToolTabStrip>().Single();
        ToolTabStripItem selected = strip.GetVisualDescendants().OfType<ToolTabStripItem>().Single(item => item.IsSelected);
        Color accent = ((ISolidColorBrush)selected.BorderBrush!).Color;
        Assert.That(control.TryFindResource("DockSeparatorBrush", control.ActualThemeVariant, out object? separatorBrush), Is.True);
        Color separator = ((ISolidColorBrush)separatorBrush!).Color;

        double scale = window.RenderScaling;
        Point stripOrigin = strip.TranslatePoint(default, window)!.Value;
        double tabOrigin = selected.TranslatePoint(default, window)!.Value.X;
        int left = (int)Math.Round(stripOrigin.X * scale);
        int right = (int)Math.Round((stripOrigin.X + strip.Bounds.Width) * scale);
        int bottom = (int)Math.Round((stripOrigin.Y + strip.Bounds.Height) * scale);
        int tabLeft = (int)Math.Round(tabOrigin * scale);
        int tabRight = (int)Math.Round((tabOrigin + selected.Bounds.Width) * scale);

        using WriteableBitmap frame = window.CaptureRenderedFrame()!;
        var problems = new List<string>();
        for (int x = left; x < right; x++)
        {
            // The pixels on either side of a tab edge may blend the two.
            if (Math.Abs(x - tabLeft) <= 1 || Math.Abs(x - tabRight) <= 1)
                continue;

            (int R, int G, int B) bottomRow = HeadlessTestHelpers.ReadPixel(frame, new PixelPoint(x, bottom - 1));
            if (x > tabLeft && x < tabRight)
            {
                (int R, int G, int B) rowAbove = HeadlessTestHelpers.ReadPixel(frame, new PixelPoint(x, bottom - 2));
                if (!Near(bottomRow, accent) || !Near(rowAbove, accent))
                    problems.Add($"x={x}: {rowAbove}/{bottomRow} under the selected tab");
            }
            else if (!Near(bottomRow, separator))
            {
                problems.Add($"x={x}: {bottomRow} beside the selected tab");
            }
        }

        Assert.That(
            problems,
            Is.Empty,
            $"The underline is broken {when} (selected tab at {tabLeft}..{tabRight} of {left}..{right}, accent {accent}, baseline {separator}).");
    }

    private static bool Near((int R, int G, int B) pixel, Color color)
    {
        const int Tolerance = 8;
        return Math.Abs(pixel.R - color.R) <= Tolerance
            && Math.Abs(pixel.G - color.G) <= Tolerance
            && Math.Abs(pixel.B - color.B) <= Tolerance;
    }
}
