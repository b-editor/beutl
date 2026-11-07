using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Editor.Components.SceneSettingsTab;
using Beutl.Extensibility;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dock;
using Beutl.Views;
using Dock.Avalonia.Controls;
using Dock.Model.Core;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class FloatingToolWindowTextTests
{
    // Dock's theme for the window a tool floats into gives the whole window the text of its own tab strip:
    // 12px, in the tabs' secondary color, in no particular font. The tool's labels inherit it, so they
    // shrank and dimmed the moment the tool left the main window.
    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task A_floated_tool_keeps_the_text_it_has_when_docked(bool light)
    {
        using ThemeVariantScope theme = ThemeVariantScope.Use(light ? ThemeVariant.Light : ThemeVariant.Dark);
        using FloatedTool floated = await FloatedTool.CreateAsync("floating-tool-text-" + (light ? "light" : "dark"));

        Assert.That(Text.Of(floated.View), Is.EqualTo(floated.Docked));
    }

    // The grip's title took its color from the window while its tool was inactive, so giving the window the
    // main window's text must not brighten it.
    [AvaloniaTest]
    public async Task A_floated_tools_grip_title_stays_dim_while_the_tool_is_inactive()
    {
        using ThemeVariantScope theme = ThemeVariantScope.Use(ThemeVariant.Dark);
        using FloatedTool floated = await FloatedTool.CreateAsync("floating-tool-grip");
        TextBlock title = floated.Window.GetVisualDescendants()
            .OfType<ToolChromeControl>()
            .Single()
            .GetVisualDescendants()
            .OfType<TextBlock>()
            .Single(block => block.Name == "PART_Title");
        var dock = (IDock)floated.Tool.Owner!;

        dock.IsActive = true;
        HeadlessTestHelpers.Render();
        Color active = ((ISolidColorBrush)title.Foreground!).Color;
        dock.IsActive = false;
        HeadlessTestHelpers.Render();
        Color inactive = ((ISolidColorBrush)title.Foreground!).Color;

        Assert.Multiple(() =>
        {
            Assert.That(active, Is.EqualTo(((ISolidColorBrush)title.FindResource("DockTabActiveForegroundBrush")!).Color));
            Assert.That(inactive, Is.EqualTo(((ISolidColorBrush)title.FindResource("DockTabForegroundBrush")!).Color));
        });
    }

    private static Control ViewOf(Visual root, BeutlToolDockable tool)
    {
        return (Control)root.GetVisualDescendants()
            .OfType<ToolTabContent>()
            .Single(content => ReferenceEquals(content.DataContext, tool))
            .Content!;
    }

    // What a label in the tool inherits.
    private readonly record struct Text(double FontSize, FontFamily FontFamily, Color Foreground)
    {
        public static Text Of(Control control)
        {
            return new Text(
                TextElement.GetFontSize(control),
                TextElement.GetFontFamily(control),
                ((ISolidColorBrush)TextElement.GetForeground(control)!).Color);
        }
    }

    // The scene settings, opened in the main window's right dock and then floated into a window of its own.
    private sealed class FloatedTool : IDisposable
    {
        private readonly Window _main;
        private readonly IHostWindow _host;

        private FloatedTool(Window main, IHostWindow host, BeutlToolDockable tool, Text docked)
        {
            _main = main;
            _host = host;
            Tool = tool;
            Docked = docked;
        }

        public Window Window => (Window)_host;

        public BeutlToolDockable Tool { get; }

        // The tool's text before it was floated.
        public Text Docked { get; }

        public Control View => ViewOf(Window, Tool);

        public static async Task<FloatedTool> CreateAsync(string name)
        {
            await TestReset.ResetShellAsync();
            EditViewModel editor = await TestShell.OpenNewSceneEditorAsync(name);
            BeutlDockFactory factory = editor.DockHost.Factory;
            var main = new Window { Content = new EditView { DataContext = editor }, Width = 1200, Height = 700 };
            IHostWindow? host = null;
            try
            {
                main.Show();
                Assert.That(
                    editor.DockHost.OpenToolTabFromExtension(
                        SceneSettingsTabExtension.Instance, factory.GetAnchoredDock(DockAnchor.Right)),
                    Is.True);
                HeadlessTestHelpers.Render(2);
                BeutlToolDockable tool = BeutlDockFactory.Traverse(editor.DockHost.Layout.Value)
                    .OfType<BeutlToolDockable>()
                    .Single(dockable => dockable.ToolContext.Extension == SceneSettingsTabExtension.Instance);
                Text docked = Text.Of(ViewOf(main, tool));
                HeadlessCapture.SaveIfRequested(main, nameof(FloatingToolWindowTextTests), name + "-docked");

                factory.FloatDockable(tool);
                HeadlessTestHelpers.Render(2);
                host = factory.FindRoot(tool, _ => true)!.Window!.Host!;
                HeadlessCapture.SaveIfRequested((Window)host, nameof(FloatingToolWindowTextTests), name + "-floated");
                return new FloatedTool(main, host, tool, docked);
            }
            catch
            {
                host?.Exit();
                main.Close();
                HeadlessTestHelpers.Settle();
                throw;
            }
        }

        public void Dispose()
        {
            _host.Exit();
            _main.Close();
            HeadlessTestHelpers.Settle();
        }
    }
}
