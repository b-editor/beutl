using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.Editor.Components.AgentTab.ViewModels;

namespace Beutl.Editor.Components.AgentTab.Views;

internal sealed partial class AgentToolView : UserControl
{
    public AgentToolView()
    {
        InitializeComponent();
        WorkExpander.PropertyChanged += OnWorkExpanderPropertyChanged;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        SyncDetails();
    }

    private void OnInlineExpandChanged(object? sender, RoutedEventArgs e) => SyncDetails();

    private void OnWorkExpanderPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Expander.IsExpandedProperty)
            SyncDetails();
    }

    private void SyncDetails()
    {
        var item = DataContext as AgentToolItem;
        bool showInline = item is { IsBackgroundWork: false } && ToolToggle.IsChecked == true;
        InlineDetailsBorder.IsVisible = showInline;
        InlineDetailsHost.Content = showInline ? item : null;

        bool showWork = item is { IsBackgroundWork: true } && WorkExpander.IsExpanded;
        WorkDetailsHost.Content = showWork ? item : null;
    }

    private static async void OnOpenToolImage(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AgentImagePreview preview } button)
            await AgentImageLightbox.OpenAsync(button, preview);
    }
}
