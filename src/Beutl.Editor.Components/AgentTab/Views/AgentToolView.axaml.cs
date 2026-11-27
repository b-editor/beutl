using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.Editor.Components.AgentTab.ViewModels;

namespace Beutl.Editor.Components.AgentTab.Views;

internal sealed partial class AgentToolView : UserControl
{
    public AgentToolView() => InitializeComponent();

    private static async void OnOpenToolImage(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AgentImagePreview preview } button)
            await AgentImageLightboxWindow.OpenAsync(button, preview);
    }
}
