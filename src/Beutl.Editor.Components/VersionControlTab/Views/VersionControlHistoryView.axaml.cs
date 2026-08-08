using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Editor.Components.VersionControlTab.ViewModels;

namespace Beutl.Editor.Components.VersionControlTab.Views;

internal sealed partial class VersionControlHistoryView : UserControl
{
    public VersionControlHistoryView()
    {
        InitializeComponent();
    }

    internal bool DrillDownOnSelection { get; set; }

    private async void OnCommitSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!IsEffectivelyVisible
            || DataContext is not VersionControlTabViewModel viewModel
            || sender is not ListBox listBox)
        {
            return;
        }

        if (listBox.SelectedItem is VersionControlCommitViewModel commit)
        {
            if (DrillDownOnSelection)
            {
                await VersionControlViewEventBoundary.RunSafelyAsync(
                    () => viewModel.OpenCommitDetailAsync(commit));
            }
            else
            {
                await VersionControlViewEventBoundary.RunSafelyAsync(
                    () => viewModel.SelectCommitAsync(commit));
            }
        }
        else if (!DrillDownOnSelection)
        {
            await VersionControlViewEventBoundary.RunSafelyAsync(
                () => viewModel.SelectCommitAsync(null));
        }
    }

    private void OnCommitListTapped(object? sender, TappedEventArgs e)
    {
        if (!DrillDownOnSelection
            || !IsEffectivelyVisible
            || DataContext is not VersionControlTabViewModel viewModel
            || viewModel.ShowingDetail.Value
            || e.Source is not Visual source
            || source.FindAncestorOfType<ListBoxItem>(includeSelf: true)
                is not { DataContext: VersionControlCommitViewModel commit }
            || !ReferenceEquals(viewModel.SelectedCommit.Value, commit))
        {
            return;
        }

        viewModel.ShowSelectedCommitDetail();
    }
}
