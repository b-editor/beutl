using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.Editor.Components.VersionControlTab.ViewModels;

namespace Beutl.Editor.Components.VersionControlTab.Views;

internal sealed partial class VersionControlChangesView : UserControl
{
    public VersionControlChangesView()
    {
        InitializeComponent();
    }

    private async void OnChangedFileSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (IsEffectivelyVisible
            && DataContext is VersionControlTabViewModel viewModel
            && sender is ListBox listBox)
        {
            await VersionControlViewEventBoundary.RunSafelyAsync(() => viewModel.SelectFileAsync(
                listBox.SelectedItem as VersionControlFileChangeViewModel));
        }
    }

    private async void OnRestoreClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is VersionControlTabViewModel
            {
                SelectedCommit.Value: { } selectedCommit,
            } viewModel)
        {
            await VersionControlViewEventBoundary.RunSafelyAsync(
                () => viewModel.RestoreAsync(selectedCommit.Commit));
        }
    }

    private async void OnRestoreToNewBranchClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is VersionControlTabViewModel
            {
                SelectedCommit.Value: { } selectedCommit,
            } viewModel)
        {
            await VersionControlViewEventBoundary.RunSafelyAsync(
                () => viewModel.RestoreToNewBranchAsync(selectedCommit.Commit));
        }
    }
}
