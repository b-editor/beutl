using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Beutl.Controls;
using Beutl.Editor.Components.FileBrowserTab.ViewModels;
using Beutl.Editor.Components.FileBrowserTab.Views;
using Beutl.Editor.Components.ProxiesTab.ViewModels;
using Beutl.Editor.Components.ProxiesTab.Views;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class ToolTabBarViewTests
{
    [AvaloniaTest]
    [TestCase(320, false)]
    [TestCase(640, false)]
    [TestCase(320, true)]
    [TestCase(640, true)]
    public async Task BarsStayAlignedAndActionsWorkAcrossViewStates(int width, bool light)
    {
        await TestReset.ResetShellAsync();
        string root = Path.Combine(BeutlHomeIsolation.CurrentHome!, $"tool-tab-bars-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Project project = (await TestShell.Project.CreateProject(640, 480, 30, 44100, "tool-tab-bars", root))!;
        Scene scene = project.Items.OfType<Scene>().First();
        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;

        using var files = new FileBrowserTabViewModel(editor);
        using var layouts = new DockLayoutViewModel(editor, new DockLayoutPresetService(Path.Combine(root, "layouts.json")));
        using var proxies = new ProxiesTabViewModel(editor);
        var fileView = new FileBrowserTabView { DataContext = files };
        var layoutView = new DockLayoutView { DataContext = layouts };
        var proxyView = new ProxiesTabView { DataContext = proxies };
        var window = new Window
        {
            Width = width,
            Height = 500,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark
        };
        double? barHeight = null;
        try
        {
            window.Content = fileView;
            window.Show();
            CheckBar(fileView, "files-home");

            string folder = Path.Combine(root, "media", "a-long-folder-name-to-exercise-breadcrumb-overflow");
            Directory.CreateDirectory(folder);
            files.RootPath.Value = folder;
            HeadlessTestHelpers.Settle();
            Assert.That(files.IsHomeView.Value, Is.False);
            CheckBar(fileView, "files-folder");
            FileBrowserViewMode previousMode = files.ViewMode.Value;
            Click(fileView.FindControl<Button>("ViewModeButton")!);
            Assert.That(files.ViewMode.Value, Is.Not.EqualTo(previousMode));
            Click(fileView.FindControl<Button>("HomeButton")!);
            Assert.That(files.IsHomeView.Value, Is.True);

            window.Content = layoutView;
            CheckBar(layoutView, "layouts-empty");
            Assert.That(layouts.Save("Editing"), Is.Not.Null);
            CheckBar(layoutView, "layouts-saved");

            window.Content = proxyView;
            CheckBar(proxyView, "proxies");
            string longSummary = string.Join(" ", Enumerable.Repeat(Strings.ProxyQueueIdle, 20));
            proxies.ClipSummary.Value = longSummary;
            CheckBar(proxyView, "proxies-long-summary");
            Assert.That(proxies.ClipSummary.Value, Is.EqualTo(longSummary));
            Click(proxyView.FindControl<Button>("RefreshButton")!);
            Assert.That(proxies.ClipSummary.Value, Is.Not.EqualTo(longSummary),
                "refresh must still reach the view model through the shared bar");
        }
        finally { window.Close(); }

        void Click(Button button)
        {
            Point center = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseDown(center, MouseButton.Left);
            window.MouseUp(center, MouseButton.Left);
            HeadlessTestHelpers.Settle();
        }

        void CheckBar(Control view, string name)
        {
            window.UpdateLayout();
            HeadlessTestHelpers.Render();
            ToolTabBar bar = view.FindControl<ToolTabBar>("ToolBar")!;
            barHeight ??= bar.Bounds.Height;
            Assert.That(bar.Bounds.Height, Is.EqualTo(barHeight.Value), name);
            foreach (Button button in bar.GetLogicalDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible))
            {
                Point position = button.TranslatePoint(default, bar)!.Value;
                Assert.Multiple(() =>
                {
                    Assert.That(button.Bounds.Width, Is.GreaterThan(0), name);
                    Assert.That(position.X, Is.GreaterThanOrEqualTo(0), name);
                    Assert.That(position.Y, Is.GreaterThanOrEqualTo(0), name);
                    Assert.That(position.X + button.Bounds.Width, Is.LessThanOrEqualTo(bar.Bounds.Width), name);
                    Assert.That(position.Y + button.Bounds.Height, Is.LessThanOrEqualTo(bar.Bounds.Height), name);
                });
            }

            if (Environment.GetEnvironmentVariable("BEUTL_TOOL_TAB_BAR_CAPTURE") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                using var image = window.CaptureRenderedFrame();
                image?.Save(Path.Combine(directory, $"{name}-{width}-{light}.png"), PngBitmapEncoderOptions.Default);
            }
        }
    }
}
