using Avalonia.Controls;

using FluentAvalonia.UI.Windowing;

namespace Beutl.Views;

public sealed partial class MainWindow : FAAppWindow
{
    private readonly MainWindowCloseGuard _closeGuard;

    public MainWindow()
    {
        InitializeComponent();
        _closeGuard = new MainWindowCloseGuard(this, mainView);
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

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_closeGuard.CancelClosing(e))
            return;

        base.OnClosing(e);
        WindowPlacement.Save(this);
    }
}
