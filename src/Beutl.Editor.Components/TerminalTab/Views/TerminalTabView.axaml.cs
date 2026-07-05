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
            _viewModel.Disposed += OnViewModelDisposed;
            TryLaunch();
        }
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        TryLaunch();
    }

    private async void TryLaunch()
    {
        if (_launched || _launching || _viewModel == null || !IsLoaded)
        {
            return;
        }

        _launching = true;

        string? previousLang = null;
        bool langOverridden = false;
        if (_viewModel.LangFallback is { } lang)
        {
            // The PTY inherits this process' environment; there is no per-launch override.
            // Set LANG only around the spawn and restore it afterwards so the terminal-only
            // locale does not leak into later child processes (e.g. the FFmpeg worker).
            previousLang = Environment.GetEnvironmentVariable("LANG");
            Environment.SetEnvironmentVariable("LANG", lang);
            langOverridden = true;
        }

        try
        {
            await Terminal.LaunchProcess(_viewModel.WorkingDirectory, _viewModel.ShellPath, _viewModel.ShellArgs);
        }
        catch
        {
            // async void: an unhandled launch failure would terminate the app. Leaving
            // _launched false lets the restart UI retry.
            _viewModel.IsProcessExited.Value = true;
            return;
        }
        finally
        {
            if (langOverridden)
            {
                Environment.SetEnvironmentVariable("LANG", previousLang);
            }

            _launching = false;
        }

        // Set only after a successful spawn so OnViewModelDisposed knows a PTY exists to kill.
        _launched = true;

        if (_disposed)
        {
            // Torn down mid-launch: OnViewModelDisposed ran before the PTY existed, so kill
            // it here rather than reattaching a session with no live tab.
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

    private async void OnRestartClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null)
        {
            return;
        }

        _viewModel.IsProcessExited.Value = false;
        await Terminal.LaunchProcess();
        Terminal.BeginReparent();
    }

    private void OnProcessExited(object? sender, ProcessExitedEventArgs e)
    {
        if (_viewModel != null)
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
