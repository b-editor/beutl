using Avalonia.Controls;
using Beutl.Helpers;
using Beutl.ViewModels;

namespace Beutl.Views;

public sealed partial class MacWindow : Window
{
    private readonly CompositeDisposable _menuDisposables = [];
    private readonly MainWindowCloseGuard _closeGuard;
    private MacOSTitleBar? _titleBar;

    public MacWindow()
    {
        if (OperatingSystem.IsMacOS())
        {
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaTitleBarHeightHint = 40;
        }

        InitializeComponent();
        _closeGuard = new MainWindowCloseGuard(this, mainView);
        WindowPlacement.Restore(this);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _titleBar ??= MacOSTitleBar.TryAttach(this);
        WindowPlacement.FitToWorkingArea(this);

        mainView.Focus();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _menuDisposables.Clear();
        if (DataContext is MainViewModel viewModel)
            MainMenuBuilder.AddMenus(NativeMenu.GetMenu(this)!, viewModel.MenuItems, _menuDisposables);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_closeGuard.CancelClosing(e))
            return;

        base.OnClosing(e);
        WindowPlacement.Save(this);
    }

    private async void OpenTutorialsDialog(object? sender, EventArgs e) => await mainView.ShowTutorialsDialogAsync();

    private async void GoToInformationPage(object? sender, EventArgs e) => await mainView.GoToInformationPageAsync();

    private void GC_Collect_Click(object? sender, EventArgs e) => mainView.RunGcCollect();

    private void MonitorKeyModifier_Click(object? sender, EventArgs e) => mainView.OpenKeyModifierMonitor();

    private void ThrowUnhandledException_Click(object? sender, EventArgs e) => mainView.ThrowDebugException();

    private async void StartWindowCapture_Click(object? sender, EventArgs e) => await mainView.StartWindowCaptureAsync();

    private async void StopWindowCapture_Click(object? sender, EventArgs e) => await mainView.StopWindowCaptureAsync();
}
