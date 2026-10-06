using Avalonia.Controls;

using Beutl.Services;
using Beutl.ViewModels;

using FluentAvalonia.UI.Windowing;

namespace Beutl.Views;

public sealed partial class MainWindow : FAAppWindow
{
    public MainWindow()
    {
        InitializeComponent();
        WindowPlacement.Restore(this);

        TitleBar.Height = 40;
        ExtendClientAreaTitleBarHeightHint = TitleBar.Height;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        WindowPlacement.FitToWorkingArea(this);

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
                _viewModelDisposeTask = DisposeViewModelAndCloseAsync(viewModel);
            }
            return;
        }

        base.OnClosing(e);
        WindowPlacement.Save(this);
    }

    private async Task DisposeViewModelAndCloseAsync(MainViewModel viewModel)
    {
        // Publish the in-flight task before a synchronous veto can clear it.
        await Task.Yield();
        bool closeAccepted = false;
        try
        {
            if (!await viewModel.TryDisposeForWindowCloseAsync())
                return;
            closeAccepted = true;
            await viewModel.WaitForDisposalAsync();
        }
        catch (Exception ex)
        {
            await ex.Handle();
        }
        finally
        {
            if (closeAccepted)
            {
                // Once closing is accepted, a cached cleanup failure cannot veto exit.
                _viewModelDisposed = true;
                Close();
            }
            else
            {
                _viewModelDisposeTask = null;
            }
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
