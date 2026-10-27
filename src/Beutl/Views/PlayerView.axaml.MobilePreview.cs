using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.ViewModels;

namespace Beutl.Views;

public partial class PlayerView
{
    internal MobilePreviewWindow? MobilePreview { get; private set; }

    private void OnMobilePreviewClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PlayerViewModel player || TopLevel.GetTopLevel(this) is not Window owner) return;
        if (MobilePreview is { } existing) { existing.Activate(); return; }
        var window = new MobilePreviewWindow(player);
        MobilePreview = window;
        window.Closed += (_, _) => MobilePreview = null;
        window.Show(owner);
    }
}
