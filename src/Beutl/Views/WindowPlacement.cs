using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Beutl.Configuration;

namespace Beutl.Views;

// Restores, fits and saves the bounds of a main window through ViewConfig.
internal static class WindowPlacement
{
    public static void Restore(Window window)
    {
        ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;
        (int X, int Y)? pos = viewConfig.WindowPosition;
        (int Width, int Height)? size = viewConfig.WindowSize;

        if (viewConfig.IsWindowMaximized == true)
        {
            window.WindowState = WindowState.Maximized;
        }
        else if (pos.HasValue && size.HasValue)
        {
            var rect = new PixelRect(pos.Value.X, pos.Value.Y, size.Value.Width, size.Value.Height);
            SetRect(window, rect);
        }
    }

    public static void FitToWorkingArea(Window window)
    {
        if (window.WindowState == WindowState.Maximized)
            return;

        // A window that no screen shows, because a display was unplugged or rearranged after the
        // position was saved, is brought onto the primary screen.
        Screen? screen = window.Screens.ScreenFromWindow(window);
        bool offScreen = screen == null;
        screen ??= window.Screens.Primary ?? window.Screens.All.FirstOrDefault();
        if (screen == null)
            return;

        // Moved there before it is measured, so it already has that screen's scaling.
        if (offScreen)
            window.Position = screen.WorkingArea.Position;

        // The position and the working area are in screen pixels, the size in device-independent units.
        double scaling = window.DesktopScaling;
        PixelRect area = screen.WorkingArea;
        var rect = new PixelRect(window.Position, PixelSize.FromSize(window.ClientSize, scaling));
        if (!offScreen && area.Contains(rect))
            return;

        rect = rect.WithWidth(Math.Min(area.Width, rect.Width)).WithHeight(Math.Min(area.Height, rect.Height));
        rect = area.CenterRect(rect);
        window.Position = rect.Position;
        window.Width = rect.Width / scaling;
        window.Height = rect.Height / scaling;
    }

    public static void Save(Window window)
    {
        ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;
        viewConfig.WindowSize = ((int)window.ClientSize.Width, (int)window.ClientSize.Height);
        viewConfig.WindowPosition = (window.Position.X, window.Position.Y);
        viewConfig.IsWindowMaximized = window.WindowState == WindowState.Maximized;
    }

    private static void SetRect(Window window, PixelRect rect)
    {
        window.Position = rect.Position;
        window.Width = rect.Width;
        window.Height = rect.Height;
    }
}
