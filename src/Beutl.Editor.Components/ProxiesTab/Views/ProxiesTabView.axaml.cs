using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Beutl.Editor.Components.ProxiesTab.ViewModels;

namespace Beutl.Editor.Components.ProxiesTab.Views;

public sealed partial class ProxiesTabView : UserControl
{
    public ProxiesTabView()
    {
        InitializeComponent();
    }

    private void OnProxyItemTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is not Visual source
            || IsInteractiveControl(source)
            || sender is not Control { DataContext: ProxyClipViewModel clip })
            return;

        clip.ToggleSelection();
        e.Handled = true;
    }

    private static bool IsInteractiveControl(Visual source)
    {
        return source.FindAncestorOfType<Button>(includeSelf: true) != null
            || source.FindAncestorOfType<ComboBox>(includeSelf: true) != null
            || source.FindAncestorOfType<CheckBox>(includeSelf: true) != null;
    }
}
