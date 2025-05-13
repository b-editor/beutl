using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Beutl.ViewModels;

namespace Beutl.Views;

public partial class TitleBreadcrumbBar : UserControl
{
    public TitleBreadcrumbBar()
    {
        InitializeComponent();
    }

    private void OnButtonClick(object? sender, RoutedEventArgs e)
    {
        // Commandプロパティを使わない理由
        // - flyout.Hideを実行するとbuttonのDataContextとCommandがnullになり実行されなくなってしまうため
        if (sender is Button button && DataContext is TitleBreadcrumbBarViewModel viewModel)
        {
            switch (button.Tag)
            {
                case "OpenFile":
                    viewModel.OpenFile.Execute(null);
                    break;
                case "NewScene":
                    viewModel.NewScene.Execute(null);
                    break;
            }
        }

        if (FileButton.Flyout is Flyout flyout)
        {
            flyout.Hide();
        }
    }

    private void OnListBoxTapped(object? sender, TappedEventArgs e)
    {
        if (FileButton.Flyout is not Flyout flyout) return;
        if ((e.Source as StyledElement)?.GetSelfAndLogicalAncestors().Any(i => i is ListBoxItem) == true)
        {
            flyout.Hide();
        }
    }
}
