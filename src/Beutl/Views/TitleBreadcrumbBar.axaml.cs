using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Beutl.Services;
using Beutl.ViewModels;

namespace Beutl.Views;

public partial class TitleBreadcrumbBar : UserControl
{
    public TitleBreadcrumbBar()
    {
        InitializeComponent();
    }

    private async void OnButtonClick(object? sender, RoutedEventArgs e)
    {
        // Commandプロパティを使わない理由
        // - flyout.Hideを実行するとbuttonのDataContextとCommandがnullになり実行されなくなってしまうため
        Func<Task>? execute = null;
        if (sender is Button button && DataContext is TitleBreadcrumbBarViewModel viewModel)
        {
            switch (button.Tag)
            {
                case "OpenFile":
                    execute = () => viewModel.OpenFile.ExecuteAsync(null!);
                    break;
                case "NewScene":
                    execute = () => viewModel.NewScene.ExecuteAsync(null!);
                    break;
            }
        }

        if (FileButton.Flyout is Flyout flyout)
        {
            flyout.Hide();
        }

        if (execute is not null)
        {
            try
            {
                await execute();
            }
            catch (Exception ex)
            {
                await ex.Handle();
            }
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

    private void OnSelectedTabItemChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox
            || listBox.SelectedItem is not EditorTabItem item
            || DataContext is not TitleBreadcrumbBarViewModel viewModel
            || ReferenceEquals(viewModel.SelectedTabItem.Value, item))
        {
            return;
        }

        if (!viewModel.ActivateTabItem(item))
            listBox.SelectedItem = viewModel.SelectedTabItem.Value;
    }
}
