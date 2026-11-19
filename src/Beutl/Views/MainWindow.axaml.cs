using Avalonia.Controls;
using Beutl.Helpers;
using Beutl.ViewModels;
using FluentAvalonia.UI.Windowing;

namespace Beutl.Views;

public sealed partial class MainWindow : FAAppWindow
{
    private readonly MainWindowCloseGuard _closeGuard;
    private WindowsAutoHideTaskbar? _autoHideTaskbar;

    public MainWindow()
    {
        InitializeComponent();
        _closeGuard = new MainWindowCloseGuard(this, mainView);

        TitleBar.Height = 40;
        ExtendClientAreaTitleBarHeightHint = TitleBar.Height;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MainViewModel viewModel)
            WindowPlacement.Restore(this, viewModel.Configuration.ViewConfig);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _autoHideTaskbar ??= WindowsAutoHideTaskbar.TryAttach(this);
        WindowPlacement.FitToWorkingArea(this);

        mainView.Focus();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_closeGuard.CancelClosing(e))
            return;

        base.OnClosing(e);
        if (DataContext is MainViewModel viewModel)
            WindowPlacement.Save(this, viewModel.Configuration.ViewConfig);
    }
}
