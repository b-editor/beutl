using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
using Beutl.Configuration;
using Beutl.PackageTools.UI.Views;
using Beutl.Pages;
using Beutl.Testing.Headless;
using Beutl.Views;
using FluentAvalonia.UI.Windowing;

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
            config.WindowPosition = previous.WindowPosition;
            config.WindowSize = previous.WindowSize;
            config.IsWindowMaximized = previous.IsWindowMaximized;
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

    [AvaloniaTest]
    public async Task App_windows_can_be_opened_together_and_recreated_in_runtime_mode()
    {
        var lifecycleDesignModes = new List<bool>();
        for (int i = 0; i < 2; i++)
        {
            var first = HeadlessAppWindow.Create(() => new FAAppWindow());
            FAAppWindow? second = null;
            try
            {
                second = HeadlessAppWindow.Create(() => new FAAppWindow());
                foreach (FAAppWindow window in new[] { first, second })
                {
                    window.Opened += (_, _) => lifecycleDesignModes.Add(Design.IsDesignMode);
                    window.Closing += (_, _) => lifecycleDesignModes.Add(Design.IsDesignMode);
                    window.Content = new TextBlock { Text = "Headless app window" };
                    window.Show();
                }
                HeadlessTestHelpers.Render();
                Assert.Multiple(() =>
                {
                    Assert.That(Design.IsDesignMode, Is.False);
                    Assert.That(first.IsVisible, Is.True);
                    Assert.That(second.IsVisible, Is.True);
                });
            }
            finally
            {
                if (second is not null)
                    await CloseAsync(second);
                await CloseAsync(first);
            }
        }
        Assert.That(lifecycleDesignModes, Is.EqualTo(Enumerable.Repeat(false, 8)));
    }

    [AvaloniaTest]
    public void Failed_app_window_construction_restores_runtime_mode()
    {
        var failure = new InvalidOperationException("Window construction failed.");
        var thrown = Assert.Throws<InvalidOperationException>(() =>
            HeadlessAppWindow.Create<FAAppWindow>(() => throw failure));
        Assert.Multiple(() =>
        {
            Assert.That(thrown, Is.SameAs(failure));
            Assert.That(Design.IsDesignMode, Is.False);
        });
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
