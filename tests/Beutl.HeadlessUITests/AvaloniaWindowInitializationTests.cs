using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
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
        var previous = (WindowPosition: config.WindowPosition.Value, WindowSize: config.WindowSize.Value, IsWindowMaximized: config.IsWindowMaximized.Value);
        Func<Window>[] factories =
        [
            () => HeadlessAppWindow.Create(() => new Beutl.Views.MainWindow()),
            () => new MacWindow(),
            () => HeadlessAppWindow.Create(() => new KeyModifierMonitor()),
            () => HeadlessAppWindow.Create(() => new SettingsDialog()),
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
            config.WindowPosition.Value = previous.WindowPosition;
            config.WindowSize.Value = previous.WindowSize;
            config.IsWindowMaximized.Value = previous.IsWindowMaximized;
        }
    }

    [AvaloniaTest]
    public async Task Key_monitor_runs_the_base_window_opening_lifecycle()
    {
        var window = HeadlessAppWindow.Create(() => new KeyModifierMonitor());
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
    public async Task Key_monitor_shows_what_each_key_types()
    {
        var owner = new Window();
        var monitor = HeadlessAppWindow.Create(() => new KeyModifierMonitor());
        try
        {
            owner.Show();
            monitor.Show(owner);
            HeadlessTestHelpers.Render();

            owner.KeyPress(Key.Oem1, RawInputModifiers.None, PhysicalKey.Semicolon, ";");
            owner.KeyPress(Key.A, RawInputModifiers.None, PhysicalKey.A, "a");
            owner.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            owner.KeyPress(Key.F1, RawInputModifiers.None, PhysicalKey.F1, null);
            owner.KeyPress(Key.D1, RawInputModifiers.Shift, PhysicalKey.Digit1, "!");
            owner.KeyPress(Key.D1, RawInputModifiers.None, PhysicalKey.Digit1, "1");
            HeadlessTestHelpers.Render();

            Assert.That(monitor.GetVisualDescendants().OfType<Button>().Select(button => button.Content),
                Is.EqualTo(new[] { ";", "A", "Space", "F1", "1" }));
        }
        finally
        {
            await CloseAsync(monitor);
            owner.Close();
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
