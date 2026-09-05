using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

using Beutl.Configuration;
using Beutl.Services;
using Beutl.ViewModels;

using FluentAvalonia.UI.Windowing;

namespace Beutl.Views;

public sealed partial class MainWindow : AppWindow
{
    public MainWindow()
    {
        InitializeComponent();
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

    private bool _captureStopped;
    private Task? _captureStopTask;
    private bool _viewModelDisposed;
    private Task? _viewModelDisposeTask;

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_captureStopped)
        {
            if (_captureStopTask is not null)
            {
                // A shutdown task is already draining ffmpeg from a prior close
                // attempt; keep cancelling until that task finalizes and calls Close().
                e.Cancel = true;
                return;
            }

            if (mainView is { HasActiveCapture: true } mv)
            {
                e.Cancel = true;
                _captureStopTask = StopCaptureAndCloseAsync(mv);
                return;
            }
        }

        if (!_viewModelDisposed && DataContext is MainViewModel viewModel)
        {
            e.Cancel = true;
            if (_viewModelDisposeTask is null)
            {
                viewModel.Dispose();
                _viewModelDisposeTask = DisposeViewModelAndCloseAsync(viewModel);
            }
            return;
        }

        base.OnClosing(e);
        ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;
        viewConfig.WindowSize = ((int)ClientSize.Width, (int)ClientSize.Height);
        viewConfig.WindowPosition = (Position.X, Position.Y);
        viewConfig.IsWindowMaximized = WindowState == WindowState.Maximized;
    }

    private async Task DisposeViewModelAndCloseAsync(MainViewModel viewModel)
    {
        try
        {
            await viewModel.WaitForDisposalAsync();
        }
        finally
        {
            _viewModelDisposed = true;
            Close();
        }
    }

    private async Task StopCaptureAndCloseAsync(MainView mv)
    {
        try
        {
            await mv.EnsureCaptureStoppedAsync();
        }
        catch (Exception ex)
        {
            await ex.Handle();
        }
        finally
        {
            _captureStopped = true;
            Close();
        }
    }
}
