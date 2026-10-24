using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Extensibility;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class DockTabContextMenuTests
{
    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Tab_menu_text_uses_menu_colors_regardless_of_tab_selection(bool light)
    {
        using ThemeVariantScope theme = ThemeVariantScope.Use(light ? ThemeVariant.Light : ThemeVariant.Dark);
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("tab-menu-foreground");
        var view = new EditView { DataContext = editor };
        var window = new Window { Content = view, Width = 1200, Height = 700 };
        ContextMenu? menu = null;

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            IToolDock dock = editor.DockHost.Factory.GetAnchoredDock(DockAnchor.Left)!;
            IDockable target = dock.VisibleDockables![0];

            using var assertions = Assert.EnterMultipleScope();
            foreach (bool selected in new[] { false, true, false })
            {
                editor.DockHost.Factory.SetActiveDockable(selected ? target : dock.VisibleDockables[1]);
                HeadlessTestHelpers.Render();
                ToolTabStripItem tab = view.GetVisualDescendants().OfType<ToolTabStripItem>()
                    .Single(item => ReferenceEquals(item.DataContext, target));
                Assert.That(tab.IsSelected, Is.EqualTo(selected));

                Control menuHost = tab.GetVisualDescendants().OfType<Control>().First(c => c.ContextMenu != null);
                menu = menuHost.ContextMenu!;
                HeadlessTestHelpers.Click(tab, new Point(8, tab.Bounds.Height / 2), MouseButton.Right, move: true);
                Assert.That(menu.IsOpen, Is.True);
                HeadlessCapture.SaveIfRequested(menu, nameof(DockTabContextMenuTests),
                    $"{(light ? "light" : "dark")}-{(selected ? "selected" : "unselected")}");

                MenuItem[] items = menu.Items.OfType<MenuItem>().Where(item => item.IsVisible).ToArray();
                Assert.That(items.Any(item => item.IsEffectivelyEnabled), Is.True);
                Assert.That(items.Any(item => !item.IsEffectivelyEnabled), Is.True);
                foreach (MenuItem item in items)
                {
                    ContentPresenter header = item.GetVisualDescendants().OfType<ContentPresenter>()
                        .Single(presenter => presenter.Name == "PART_HeaderPresenter");
                    string resource = item.IsEffectivelyEnabled
                        ? "MenuFlyoutItemForeground"
                        : "MenuFlyoutItemForegroundDisabled";
                    var actual = (ISolidColorBrush)header.Foreground!;
                    var expected = (ISolidColorBrush)item.FindResource(item.ActualThemeVariant, resource)!;
                    Assert.That((actual.Color, actual.Opacity), Is.EqualTo((expected.Color, expected.Opacity)),
                        $"{item.Header}: menu text must not inherit the tab's foreground (selected: {selected}).");
                }

                menu.Close();
                HeadlessTestHelpers.Settle();
            }
        }
        finally
        {
            menu?.Close();
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }
}
