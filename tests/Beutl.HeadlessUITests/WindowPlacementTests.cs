using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Beutl.Configuration;
using Beutl.Testing.Headless;
using Beutl.Views;

namespace Beutl.HeadlessUITests;

// The headless platform has one 1920x1280 screen at the origin, and it is the primary one.
[TestFixture, NonParallelizable]
public class WindowPlacementTests
{
    [AvaloniaTest]
    public void A_window_that_no_screen_shows_is_centered_on_the_primary_screen()
    {
        // Saved on a display that has since been unplugged.
        var window = new Window { Width = 800, Height = 600, Position = new PixelPoint(5000, -3000) };
        try
        {
            window.Show();
            Assert.That(window.Screens.ScreenFromWindow(window), Is.Null, "precondition: no screen shows the window");

            WindowPlacement.FitToWorkingArea(window);

            Assert.That(window.Position, Is.EqualTo(new PixelPoint(560, 340)));
            Assert.That((window.Width, window.Height), Is.EqualTo((800d, 600d)));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task An_editor_window_saved_on_an_unplugged_display_opens_on_the_primary_screen(bool mac)
    {
        ViewConfig config = GlobalConfiguration.Instance.ViewConfig;
        var previous = (config.WindowPosition, config.WindowSize, config.IsWindowMaximized);
        config.WindowPosition = (5000, -3000);
        config.WindowSize = (800, 600);
        config.IsWindowMaximized = false;
        Window window = mac ? new MacWindow() : HeadlessAppWindow.Create(() => new MainWindow());
        window.Content = null;
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            Assert.That(window.Position, Is.EqualTo(new PixelPoint(560, 340)));
            Assert.That((window.Width, window.Height), Is.EqualTo((800d, 600d)));
        }
        finally
        {
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            window.Closed += (_, _) => closed.TrySetResult();
            window.Close();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            HeadlessTestHelpers.Settle();
            config.WindowPosition = previous.WindowPosition;
            config.WindowSize = previous.WindowSize;
            config.IsWindowMaximized = previous.IsWindowMaximized;
        }
    }

    [AvaloniaTest]
    public void A_window_larger_than_a_scaled_screen_is_shrunk_to_its_pixels()
    {
        // 1200x800 device-independent units are 2400x1600 pixels at 200%, more than the screen holds.
        var window = new Window { Width = 1200, Height = 800, Position = new PixelPoint(100, 100) };
        try
        {
            window.Show();
            window.SetRenderScaling(2);

            WindowPlacement.FitToWorkingArea(window);

            Assert.That(window.Position, Is.EqualTo(new PixelPoint(0, 0)));
            Assert.That((window.Width, window.Height), Is.EqualTo((960d, 640d)));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void A_window_inside_the_working_area_stays_where_it_is()
    {
        var window = new Window { Width = 800, Height = 600, Position = new PixelPoint(100, 50) };
        try
        {
            window.Show();
            window.SetRenderScaling(1.5);

            WindowPlacement.FitToWorkingArea(window);

            Assert.That(window.Position, Is.EqualTo(new PixelPoint(100, 50)));
            Assert.That((window.Width, window.Height), Is.EqualTo((800d, 600d)));
        }
        finally
        {
            window.Close();
        }
    }
}
