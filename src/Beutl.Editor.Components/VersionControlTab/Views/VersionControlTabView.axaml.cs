using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.Editor.Components.VersionControlTab.ViewModels;
using Beutl.Editor.VersionControl;

namespace Beutl.Editor.Components.VersionControlTab.Views;

public sealed partial class VersionControlTabView : UserControl
{
    public VersionControlTabView()
    {
        InitializeComponent();
    }

    private async void OnCommitSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is VersionControlTabViewModel viewModel
            && sender is ListBox listBox)
        {
            await viewModel.SelectCommitAsync(
                listBox.SelectedItem as VersionControlCommitViewModel);
        }
    }

    private async void OnChangedFileSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is VersionControlTabViewModel viewModel
            && sender is ListBox listBox)
        {
            await viewModel.SelectFileAsync(
                listBox.SelectedItem as VersionControlFileChangeViewModel);
        }
    }

    private async void OnBranchSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is VersionControlTabViewModel viewModel
            && sender is ComboBox comboBox)
        {
            await viewModel.SelectBranchAsync(comboBox.SelectedItem as BranchInfo);
        }
    }
}
