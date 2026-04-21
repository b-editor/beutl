using Avalonia.Controls;
using Avalonia.Input;
using Beutl.Editor.Components.AudioVisualizerTab.ViewModels;

namespace Beutl.Editor.Components.AudioVisualizerTab.Views;

public sealed partial class AudioVisualizerTabView : UserControl
{
    public AudioVisualizerTabView()
    {
        InitializeComponent();
    }

    // Right-click on a setting control resets it to its default. The control's
    // Tag carries the view-model property name to reset; this keeps the wiring
    // declarative in XAML without needing per-control click handlers.
    private void OnSettingPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control) return;
        if (control.Tag is not string propertyName) return;
        if (DataContext is not AudioVisualizerTabViewModel vm) return;

        var props = e.GetCurrentPoint(control).Properties;
        if (!props.IsRightButtonPressed) return;

        vm.ResetSetting(propertyName);
        e.Handled = true;
    }
}
