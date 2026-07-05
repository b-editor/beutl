using Avalonia.Controls;
using Avalonia.Interactivity;

using Beutl.Editor.Components.TerminalTab.ViewModels;

using Iciclecreek.Terminal;

namespace Beutl.Editor.Components.TerminalTab.Views;

public partial class TerminalTabView : UserControl
{
    private TerminalTabViewModel? _viewModel;
    private bool _launched;

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
        if (_launched || _viewModel == null || !IsLoaded)
        {
            return;
        }

        _launched = true;
        if (_viewModel.LangFallback is { } lang)
        {
            // The PTY inherits this process' environment; there is no per-launch override.
            Environment.SetEnvironmentVariable("LANG", lang);
        }

        await Terminal.LaunchProcess(_viewModel.WorkingDirectory, _viewModel.ShellPath, _viewModel.ShellArgs);

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
