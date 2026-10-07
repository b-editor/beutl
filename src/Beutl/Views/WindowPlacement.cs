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
        Screen? screen = window.Screens.ScreenFromWindow(window);
        if (screen != null && window.WindowState != WindowState.Maximized)
        {
            var rect = new PixelRect(window.Position, PixelSize.FromSize(window.ClientSize, 1));
            if (!screen.WorkingArea.Contains(rect))
            {
                int width = Math.Min(screen.WorkingArea.Width, rect.Width);
                int height = Math.Min(screen.WorkingArea.Height, rect.Height);
                rect = rect.WithWidth(width).WithHeight(height);

                rect = screen.WorkingArea.CenterRect(rect);
                SetRect(window, rect);
            }
        }
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
