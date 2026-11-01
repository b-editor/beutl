using Avalonia;
using Avalonia.Controls;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.ViewModels.SettingsPages;

namespace Beutl.Pages.SettingsPages;

public sealed partial class EditorSettingsPage : UserControl
{
    // Windows only: the settings window and the display it is on, whose reported peak the override replaces.
    private WindowBase? _window;
    private WindowsDisplayLuminance? _display;

    public EditorSettingsPage()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // A headless window has no HWND.
        if (!OperatingSystem.IsWindows() || TopLevel.GetTopLevel(this) is not WindowBase window
            || window.TryGetPlatformHandle() is not { Handle: not 0 } handle)
        {
            return;
        }

        _window = window;
        _display = new WindowsDisplayLuminance(handle.Handle);
        // Windows sends no message when a display's HDR settings change, so the display is read again when the window
        // comes back from the Windows settings or moves to another display.
        window.Activated += OnWindowChanged;
        window.PositionChanged += OnWindowChanged;
        ShowDisplay();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_window is not null)
        {
            _window.Activated -= OnWindowChanged;
            _window.PositionChanged -= OnWindowChanged;
            _window = null;
        }

        if (OperatingSystem.IsWindows())
            _display?.Dispose();
        _display = null;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        ShowDisplay();
    }

    private void OnWindowChanged(object? sender, EventArgs e)
    {
        ShowDisplay();
    }

    private void ShowDisplay()
    {
        if (!OperatingSystem.IsWindows() || _display == null || DataContext is not EditorSettingsPageViewModel viewModel)
            return;

        _display.Refresh();
        viewModel.ShowReportedDisplay(_display.Report);
    }
}
