using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
using Beutl.Configuration;
using Beutl.PackageTools.UI.Views;
using Beutl.Pages;
using Beutl.Testing.Headless;
using Beutl.Views;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class AvaloniaWindowInitializationTests
{
    [AvaloniaTest]
    public async Task Editor_windows_can_be_constructed_and_closed_before_being_shown()
    {
        ViewConfig config = GlobalConfiguration.Instance.ViewConfig;
        var previous = (config.WindowPosition, config.WindowSize, config.IsWindowMaximized);
        Func<Window>[] factories =
        [
            () => new Beutl.Views.MainWindow(),
            () => new MacWindow(),
            () => new KeyModifierMonitor(),
            () => new SettingsDialog(),
            () => new ExtensionsPage(),
        ];

        try
        {
            foreach (Func<Window> factory in factories)
            {
                Window window = factory();
                try
                {
                    Assert.That(window.Content, Is.Not.Null, window.GetType().FullName);
                }
                finally
                {
                    await CloseAsync(window);
                }
            }
        }
        finally
        {
            config.WindowPosition = previous.WindowPosition;
            config.WindowSize = previous.WindowSize;
            config.IsWindowMaximized = previous.IsWindowMaximized;
        }
    }

    [AvaloniaTest]
    public async Task Key_monitor_runs_the_base_window_opening_lifecycle()
    {
        var window = new KeyModifierMonitor();
        int opened = 0;
        window.Opened += (_, _) => opened++;
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Assert.That(opened, Is.EqualTo(1));
        }
        finally
        {
            await CloseAsync(window);
        }
    }

    [AvaloniaTest]
    public async Task Package_tools_first_page_is_visible_on_open()
    {
        var window = new Beutl.PackageTools.UI.Views.MainWindow();
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);

            DisplayPackagesPage? page = window.GetVisualDescendants().OfType<DisplayPackagesPage>().SingleOrDefault();
            Assert.That(page, Is.Not.Null, "The initial package list must render without another navigation request.");
            Assert.That(page!.DataContext, Is.SameAs(window.DataContext));
        }
        finally
        {
            await CloseAsync(window);
        }
    }

    private static async Task CloseAsync(Window window)
    {
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        window.Close();
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        HeadlessTestHelpers.Settle();
    }
}
