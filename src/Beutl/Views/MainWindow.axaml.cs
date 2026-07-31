using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

using Beutl.Configuration;
using Beutl.ViewModels;

using FluentAvalonia.UI.Windowing;

namespace Beutl.Views;

public sealed partial class MainWindow : AppWindow
{
    private readonly WindowShutdownCoordinator _shutdown;

    public MainWindow()
    {
        InitializeComponent();
        _shutdown = new WindowShutdownCoordinator(ShutdownAsync, Close);
        ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;
        (int X, int Y)? pos = viewConfig.WindowPosition;
        (int Width, int Height)? size = viewConfig.WindowSize;

        if (viewConfig.IsWindowMaximized == true)
        {
            WindowState = WindowState.Maximized;
        }
        else if (pos.HasValue && size.HasValue)
        {
            var rect = new PixelRect(pos.Value.X, pos.Value.Y, size.Value.Width, size.Value.Height);
            SetRect(rect);
        }

        TitleBar.Height = 40;
#if DEBUG
        this.AttachDevTools();
#endif
    }

    private void SetRect(PixelRect rect)
    {
        Position = rect.Position;
        Width = rect.Width;
        Height = rect.Height;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Screen? screen = Screens.ScreenFromWindow(this);
        if (screen != null && WindowState != WindowState.Maximized)
        {
            var rect = new PixelRect(Position, PixelSize.FromSize(ClientSize, 1));
            if (!screen.WorkingArea.Contains(rect))
            {
                int width = Math.Min(screen.WorkingArea.Width, rect.Width);
                int height = Math.Min(screen.WorkingArea.Height, rect.Height);
                rect = rect.WithWidth(width).WithHeight(height);

                rect = screen.WorkingArea.CenterRect(rect);
                SetRect(rect);
            }
        }

        mainView.Focus();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_shutdown.CanClose)
        {
            e.Cancel = true;
            _ = _shutdown.BeginShutdownAsync();
            return;
        }

        base.OnClosing(e);
        ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;
        viewConfig.WindowSize = ((int)ClientSize.Width, (int)ClientSize.Height);
        viewConfig.WindowPosition = (Position.X, Position.Y);
        viewConfig.IsWindowMaximized = WindowState == WindowState.Maximized;
    }

    private async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        await mainView.EnsureCaptureStoppedAsync();
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.ShutdownAsync(cancellationToken);
        }
    }
}
