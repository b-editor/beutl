using Avalonia.Controls;
using Avalonia.Interactivity;

using Beutl.Editor.Components.TerminalTab.ViewModels;

using Iciclecreek.Terminal;

namespace Beutl.Editor.Components.TerminalTab.Views;

public partial class TerminalTabView : UserControl
{
    private TerminalTabViewModel? _viewModel;
    private bool _launching;
    private bool _launched;
    private bool _disposed;

    public TerminalTabView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel != null)
        {
            _viewModel.Disposed -= OnViewModelDisposed;
        }

        _viewModel = DataContext as TerminalTabViewModel;
        if (_viewModel != null)
        {
            if (_viewModel.LangFallback is { } lang)
            {
                // Pass the locale per spawn so it never mutates the shared process environment
                // (which would race across concurrent terminals and leak into other subprocesses).
                Terminal.EnvironmentOverrides = new Dictionary<string, string> { ["LANG"] = lang };
            }

            _viewModel.Disposed += OnViewModelDisposed;
            TryLaunch();
        }
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        TryLaunch();
    }

    private void TryLaunch()
    {
        if (_launched || _launching || _viewModel == null || !IsLoaded)
        {
            return;
        }

        // The first launch resolves the shell/args/working directory from the view-model.
        _ = LaunchAsync(reuseConfiguredProcess: false);
    }

    private void OnRestartClick(object? sender, RoutedEventArgs e)
    {
        if (_launching || _viewModel == null)
        {
            return;
        }

        // Restart reuses the shell/args/working directory the first launch already configured.
        _ = LaunchAsync(reuseConfiguredProcess: true);
    }

    private async Task LaunchAsync(bool reuseConfiguredProcess)
    {
        TerminalTabViewModel viewModel = _viewModel!;
        _launching = true;
        viewModel.IsProcessExited.Value = false;

        // The locale fallback is applied per spawn via Terminal.EnvironmentOverrides (set when the
        // view-model is attached), so no process-wide environment mutation happens here.
        try
        {
            if (reuseConfiguredProcess)
            {
                await Terminal.LaunchProcess();
            }
            else
            {
                await Terminal.LaunchProcess(viewModel.WorkingDirectory, viewModel.ShellPath, viewModel.ShellArgs);
            }
        }
        finally
        {
            _launching = false;
        }

        if (!Terminal.HasProcess)
        {
            // The terminal control swallows spawn failures (e.g. a missing SHELL/COMSPEC), so no
            // PTY exists. Surface it through the restart UI and leave the tab relaunchable.
            _launched = false;
            viewModel.IsProcessExited.Value = true;
            return;
        }

        // Set only after a confirmed live PTY so OnViewModelDisposed knows there is one to kill.
        _launched = true;

        if (_disposed)
        {
            // Torn down mid-launch: OnViewModelDisposed ran before the PTY existed, so kill it
            // here rather than reattaching a session with no live tab.
            try
            {
                Terminal.Kill();
            }
            catch
            {
                // The PTY may already be gone; tearing down must not throw.
            }

            return;
        }

        // Keep the PTY session alive while the tab is re-docked; the process is
        // killed explicitly when the view-model (i.e. the tab) is disposed.
        Terminal.BeginReparent();
    }

    private void OnProcessExited(object? sender, ProcessExitedEventArgs e)
    {
        if (_viewModel != null && !_disposed)
        {
            _viewModel.ExitCode.Value = e.ExitCode;
            _viewModel.IsProcessExited.Value = true;
        }
    }

    private void OnViewModelDisposed(object? sender, EventArgs e)
    {
        _disposed = true;
        Terminal.EndReparent();
        if (_launched)
        {
            try
            {
                Terminal.Kill();
            }
            catch
            {
                // The PTY may already be gone; tearing down the tab must not throw.
            }
        }
    }
}
