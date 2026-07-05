using Avalonia.Controls;
using Avalonia.Interactivity;

using Beutl.Editor.Components.TerminalTab.ViewModels;

using Iciclecreek.Terminal;

namespace Beutl.Editor.Components.TerminalTab.Views;

public partial class TerminalTabView : UserControl
{
    private TerminalTabViewModel? _viewModel;
    private TerminalTabViewModel? _launchedViewModel;
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

        TerminalTabViewModel? previous = _viewModel;
        if (previous != null)
        {
            previous.Disposed -= OnViewModelDisposed;
        }

        _viewModel = DataContext as TerminalTabViewModel;
        if (_viewModel != null)
        {
            if (!ReferenceEquals(_launchedViewModel, _viewModel))
            {
                // Reset the lifecycle flags only when a recycled view is bound to a DIFFERENT
                // view-model than the one it launched a PTY for. Comparing against the launched
                // view-model (not the previous DataContext) also preserves the session across a
                // VM -> null -> same-VM re-fire, where the DataContext momentarily goes null during
                // a dock/reparent.
                _launched = false;
                _launching = false;
                _disposed = false;
            }

            // Pass the locale per spawn so it never mutates the shared process environment
            // (which would race across concurrent terminals and leak into other subprocesses).
            // Cleared when the fallback is absent so a recycled view drops a prior view-model's LANG.
            Terminal.EnvironmentOverrides = _viewModel.LangFallback is { } lang
                ? new Dictionary<string, string> { ["LANG"] = lang }
                : null;

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
        if (_launched || _launching || _disposed || _viewModel == null || !IsLoaded)
        {
            return;
        }

        // The first launch resolves the shell/args/working directory from the view-model.
        _ = LaunchAsync(reuseConfiguredProcess: false);
    }

    private void OnRestartClick(object? sender, RoutedEventArgs e)
    {
        if (_launching || _disposed || _viewModel == null)
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
        // Remember which view-model this PTY serves so a later same-VM rebind (even through null)
        // is recognized and does not reset the flags and respawn.
        _launchedViewModel = viewModel;
        viewModel.IsProcessExited.Value = false;

        // Suppress detach-cleanup before spawning so switching away mid-launch re-docks the tab
        // instead of killing the just-spawned PTY. Kept on for the tab's life; disposal calls
        // EndReparent + Shutdown. The locale fallback is applied per spawn via
        // Terminal.EnvironmentOverrides, so no process-wide environment mutation happens here.
        Terminal.BeginReparent();

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

        if (_disposed)
        {
            // Torn down mid-launch: tear down any PTY that did spawn and never touch the disposed
            // view-model. Shutdown is idempotent with the teardown OnViewModelDisposed already ran.
            Terminal.Shutdown();
            return;
        }

        if (!Terminal.HasPtyConnection)
        {
            // The terminal control swallows spawn failures (e.g. a missing SHELL/COMSPEC), so no
            // PTY exists. Surface it through the restart UI and leave the tab relaunchable.
            _launched = false;
            viewModel.IsProcessExited.Value = true;
            return;
        }

        _launched = true;
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
        // Shutdown (not just Kill) so the connection and read-cancellation source are disposed even
        // when no later detach runs the cleanup — an inactive tab is already detached when closed.
        Terminal.EndReparent();
        try
        {
            Terminal.Shutdown();
        }
        catch
        {
            // The PTY may already be gone; tearing down the tab must not throw.
        }
    }
}
