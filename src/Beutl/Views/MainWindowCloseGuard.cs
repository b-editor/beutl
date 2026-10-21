using Avalonia.Controls;

using Beutl.Editor.Components.Helpers;
using Beutl.ViewModels;

namespace Beutl.Views;

// The close handling shared by MainWindow and MacWindow, whose base classes differ.
internal sealed class MainWindowCloseGuard(Window window, MainView mainView)
{
    private bool _captureStopped;
    private Task? _captureStopTask;
    private bool _viewModelDisposed;
    private Task? _viewModelDisposeTask;

    // Returns true when the close was cancelled to stop the capture or dispose the view model first.
    public bool CancelClosing(WindowClosingEventArgs e)
    {
        if (!_captureStopped)
        {
            if (_captureStopTask is not null)
            {
                // A shutdown task is already draining ffmpeg from a prior close
                // attempt; keep cancelling until that task finalizes and calls Close().
                e.Cancel = true;
                return true;
            }

            if (mainView is { HasActiveCapture: true } mv)
            {
                e.Cancel = true;
                _captureStopTask = StopCaptureAndCloseAsync(mv);
                return true;
            }
        }

        if (!_viewModelDisposed && window.DataContext is MainViewModel viewModel)
        {
            e.Cancel = true;
            if (_viewModelDisposeTask is null)
            {
                _viewModelDisposeTask = DisposeViewModelAndCloseAsync(viewModel);
            }
            return true;
        }

        return false;
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
                window.Close();
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
            window.Close();
        }
    }
}
