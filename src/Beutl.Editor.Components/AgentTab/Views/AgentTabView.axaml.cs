using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.Editor.Components.AgentTab.ViewModels;

namespace Beutl.Editor.Components.AgentTab.Views;

internal sealed partial class AgentTabView : UserControl
{
    public AgentTabView()
    {
        InitializeComponent();
    }

    internal void FocusComposer() => Composer.FocusPrompt();

    protected override async void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (DataContext is AgentTabViewModel viewModel)
            await viewModel.InitializeAsync();
    }

    private void OnStarterPrompt(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AgentStarterPrompt starter } && DataContext is AgentTabViewModel { CanManage: true } viewModel)
        {
            viewModel.ApplyStarterPrompt(starter);
            Composer.FocusPrompt();
        }
    }

    private async void OnRecentThread(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AgentThreadRow row } && DataContext is AgentTabViewModel viewModel)
            await viewModel.SelectThreadAsync(row.Thread);
    }
}
