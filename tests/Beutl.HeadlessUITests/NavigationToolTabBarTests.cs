using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Beutl.Configuration;
using Beutl.Controls;
using Beutl.Editor.Components.LibraryTab.ViewModels;
using Beutl.Editor.Components.LibraryTab.Views;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;
using FluentAvalonia.UI.Controls;
using FluentIcons.Avalonia.Fluent;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class NavigationToolTabBarTests
{
    [AvaloniaTest]
    [TestCase(320, false)]
    [TestCase(640, false)]
    [TestCase(320, true)]
    [TestCase(640, true)]
    public async Task Navigation_keeps_selection_menus_and_narrow_width_access(int width, bool light)
    {
        await TestReset.ResetShellAsync();
        string root = Path.Combine(BeutlHomeIsolation.CurrentHome!, $"navigation-bars-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Project project = (await TestShell.Project.CreateProject(640, 480, 30, 44100, "navigation-bars", root))!;
        Scene scene = project.Items.OfType<Scene>().First();
        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
        using var library = new LibraryTabViewModel(editor);
        var originalModes = library.LibraryTabDisplayModes.ToArray();
        await using AiWorkspaceViewModel workspace = TestShell.MainViewModel.CreateAiWorkspaceViewModel(editor);
        var libraryView = new LibraryTabView { DataContext = library };
        var aiView = new AiWorkspaceView { DataContext = workspace };
        var window = new Window
        {
            Content = libraryView,
            Width = width,
            Height = 480,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark
        };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            TabStrip libraryStrip = libraryView.FindControl<TabStrip>("tabStrip")!;
            var libraryItems = libraryStrip.Items.Cast<TabStripItem>().ToArray();
            CheckItems(libraryItems);
            Capture("library");
            Assert.That(libraryView.FindControl<ToolTabBar>("ToolBar")!.Bounds.Height, Is.EqualTo(38));

            Click(libraryItems[0]);
            Assert.That(libraryView.FindControl<Carousel>("carousel")!.SelectedIndex, Is.Zero);
            CheckItems(libraryItems);
            libraryItems[0].Focus();
            PressRight();
            Assert.That(libraryStrip.SelectedIndex, Is.EqualTo(1));
            CheckItems(libraryItems);

            Button more = libraryView.FindControl<Button>("moreButton")!;
            Click(more);
            var menu = (FAMenuFlyout)more.ContextFlyout!;
            Assert.That(menu.IsOpen, Is.True);
            var nodesMenuItem = menu.ItemsSource!.Cast<FAToggleMenuFlyoutItem>().Last();
            bool nodesWereVisible = libraryItems[3].IsVisible;
            Click(nodesMenuItem);
            Assert.That(libraryItems[3].IsVisible, Is.EqualTo(!nodesWereVisible));
            menu.Hide();

            foreach (string key in library.LibraryTabDisplayModes.Keys.ToArray())
                library.LibraryTabDisplayModes[key] = LibraryTabDisplayMode.Show;
            window.Width = 200;
            HeadlessTestHelpers.Render();
            ScrollViewer scroll = libraryView.FindControl<ScrollViewer>("scroll")!;
            Assert.That(scroll.Extent.Width, Is.GreaterThan(scroll.Viewport.Width));
            Point scrollCenter = scroll.TranslatePoint(new Point(20, 16), window)!.Value;
            window.MouseWheel(scrollCenter, new Vector(0, -30));
            HeadlessTestHelpers.Render();
            Assert.That(scroll.Offset.X, Is.GreaterThan(0));
            Point morePosition = more.TranslatePoint(default, scroll)!.Value;
            Assert.That(morePosition.X + more.Bounds.Width, Is.LessThanOrEqualTo(scroll.Viewport.Width + 1));
            Capture("library-scrolled");

            window.Width = width;
            window.Content = aiView;
            HeadlessTestHelpers.Render();
            TabStrip aiStrip = aiView.FindControl<TabStrip>("SectionStrip")!;
            TabStripItem[] AiItems() => aiStrip.GetLogicalDescendants().OfType<TabStripItem>().ToArray();
            Assert.That(AiItems(), Has.Length.EqualTo(workspace.Sections.Count));
            for (int i = 0; i < workspace.Sections.Count; i++)
            {
                Click(AiItems()[i]);
                Assert.That(workspace.SelectedSection.Value, Is.SameAs(workspace.Sections[i]));
                CheckItems(AiItems());
                CheckBounds(aiView, AiItems());
            }
            Capture("ai-jobs");
            Click(AiItems()[0]);
            AiItems()[0].Focus();
            PressRight();
            Assert.That(workspace.SelectedSection.Value, Is.SameAs(workspace.Sections[1]));
            CheckItems(AiItems());
            Capture("ai-image-edit");

            window.Width = 200;
            HeadlessTestHelpers.Render();
            CheckBounds(aiView, AiItems());
            Assert.That(AiItems().Select(item => item.Bounds.Y).Distinct().Count(), Is.GreaterThan(1));
            Capture("ai-wrapped");

            TextBlock longLabel = AiItems().Single(item => item.IsSelected).GetLogicalDescendants().OfType<TextBlock>().Single();
            longLabel.Text = string.Concat(Enumerable.Repeat("Long translated page name ", 8));
            HeadlessTestHelpers.Render();
            CheckBounds(aiView, AiItems());
            Capture("ai-long-label");
        }
        finally
        {
            window.Close();
            foreach (var entry in originalModes)
                library.LibraryTabDisplayModes[entry.Key] = entry.Value;
        }

        void PressRight()
        {
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            window.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            HeadlessTestHelpers.Render();
        }

        void Capture(string name)
        {
            if (Environment.GetEnvironmentVariable("BEUTL_NAVIGATION_BAR_CAPTURE") is not { Length: > 0 } directory) return;
            Directory.CreateDirectory(directory);
            using var image = window.CaptureRenderedFrame();
            image?.Save(Path.Combine(directory, $"{name}-{width}-{light}.png"), PngBitmapEncoderOptions.Default);
        }
    }

    private static void Click(Control control)
    {
        HeadlessTestHelpers.Render();
        TopLevel topLevel = TopLevel.GetTopLevel(control)!;
        Point center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), topLevel)!.Value;
        topLevel.MouseDown(center, MouseButton.Left);
        topLevel.MouseUp(center, MouseButton.Left);
        HeadlessTestHelpers.Render();
    }

    private static void CheckItems(IEnumerable<TabStripItem> items)
    {
        foreach (TabStripItem item in items.Where(item => item.IsVisible))
        {
            TextBlock label = item.GetLogicalDescendants().OfType<TextBlock>().Single();
            FluentIcon icon = item.GetLogicalDescendants().OfType<FluentIcon>().Single();
            Assert.Multiple(() =>
            {
                Assert.That(label.IsVisible, Is.EqualTo(item.IsSelected));
                Assert.That(item.FontWeight, Is.EqualTo(FontWeight.Normal));
                Assert.That(label.FontWeight, Is.EqualTo(FontWeight.Normal));
                Assert.That(item.Padding, Is.EqualTo(new Thickness(8, 3)));
                Assert.That(icon.FontSize, Is.EqualTo(16));
                Assert.That(AutomationProperties.GetName(item), Is.EqualTo(label.Text));
                Assert.That(ToolTip.GetTip(item), Is.EqualTo(label.Text));
            });
        }
    }

    private static void CheckBounds(Control view, IEnumerable<TabStripItem> items)
    {
        ToolTabBar bar = view.FindControl<ToolTabBar>("ToolBar")!;
        foreach (TabStripItem item in items)
        {
            Point position = item.TranslatePoint(default, bar)!.Value;
            Assert.Multiple(() =>
            {
                Assert.That(position.X, Is.GreaterThanOrEqualTo(bar.Padding.Left));
                Assert.That(position.Y, Is.GreaterThanOrEqualTo(bar.Padding.Top));
                Assert.That(position.X + item.Bounds.Width, Is.LessThanOrEqualTo(bar.Bounds.Width - bar.Padding.Right));
                Assert.That(position.Y + item.Bounds.Height, Is.LessThanOrEqualTo(bar.Bounds.Height - bar.Padding.Bottom));
            });
        }
    }
}
