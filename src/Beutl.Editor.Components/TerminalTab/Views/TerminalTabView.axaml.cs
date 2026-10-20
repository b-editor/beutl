using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Beutl.Editor.Components.TerminalTab.ViewModels;
using Beutl.Editor.Services;
using Iciclecreek.Terminal;

namespace Beutl.Editor.Components.TerminalTab.Views;

public partial class TerminalTabView : UserControl, IDisposable
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
        Terminal.AddHandler(TerminalView.TitleChangedEvent, OnTerminalTitleChanged);
        Terminal.AddHandler(KeyDownEvent, (_, args) =>
        {
            if (args.Key == Key.Enter)
                UsageTelemetry.Current?.Record("tool.command", "Terminal", "SubmitInput");
        }, RoutingStrategies.Tunnel);
    }

    // The view is recycled; update the view-model that owns the PTY.
    private void OnTerminalTitleChanged(object? sender, TitleChangedEventArgs e)
    {
        if (!_disposed && _launchedViewModel is { } viewModel)
        {
            viewModel.TerminalTitle.Value = e.Title;
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_disposed) return;

        _viewModel = DataContext as TerminalTabViewModel;
        if (_viewModel != null)
        {
            if (!ReferenceEquals(_launchedViewModel, _viewModel))
            {
                // Reset only when bound to a different view-model than the launched one; the DataContext
                // briefly goes null during a reparent.
                _launched = false;
                _launching = false;
            }

            // Pass the locale per spawn so it never mutates the shared process environment
            // (which would race across concurrent terminals and leak into other subprocesses).
            // Cleared when the fallback is absent so a recycled view drops a prior view-model's LANG.
            Terminal.EnvironmentOverrides = _viewModel.LangFallback is { } lang
                ? new Dictionary<string, string> { ["LANG"] = lang }
                : null;

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
        _launchedViewModel = viewModel;
        viewModel.IsProcessExited.Value = false;
        // Clear the previous session title; the new shell may not emit OSC 0/2.
        viewModel.TerminalTitle.Value = null;

        // Suppress detach-cleanup before spawning so switching away mid-launch re-docks the tab
        // instead of killing the just-spawned PTY. Kept on for the tab's life; disposal calls
        // EndReparent + Shutdown.
        Terminal.BeginReparent();

        try
        {
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
                // view-model. Shutdown is idempotent with the view teardown that already ran.
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
        catch
        {
            // Fire-and-forget: contain any fault and surface it through the restart UI, or tear down if
            // disposed.
            _launched = false;
            if (_disposed)
            {
                ShutdownQuietly();
            }
            else
            {
                viewModel.IsProcessExited.Value = true;
            }
        }
    }

    private void OnProcessExited(object? sender, ProcessExitedEventArgs e)
    {
        // Report on the view-model that owns the PTY, not the current DataContext, so a process exit
        // while the view is transiently unbound still reaches the right view-model.
        if (_launchedViewModel != null && !_disposed)
        {
            _launchedViewModel.IsProcessExited.Value = true;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Loaded -= OnLoaded;
        Terminal.RemoveHandler(TerminalView.TitleChangedEvent, OnTerminalTitleChanged);
        // Shutdown (not just Kill) so the connection and read-cancellation source are disposed even
        // when no later detach runs the cleanup — an inactive tab is already detached when closed.
        Terminal.EndReparent();
        ShutdownQuietly();

        _viewModel = null;
        _launchedViewModel = null;
    }

    private void ShutdownQuietly()
    {
        try
        {
            Terminal.Shutdown();
        }
        catch
        {
            // Best-effort teardown: the PTY may already be gone, and neither the fire-and-forget launch
            // catch nor Dispose may throw.
        }
    }
}
