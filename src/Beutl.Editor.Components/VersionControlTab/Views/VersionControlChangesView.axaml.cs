using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.Editor.Components.VersionControlTab.ViewModels;
using Beutl.Language;
using Beutl.Services;

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

internal static class VersionControlViewEventBoundary
{
    internal static Task RunSafelyAsync(Func<Task> operation)
    {
        return RunSafelyAsync(
            operation,
            static exception => NotificationService.ShowError(
                Strings.VersionControl_ErrorTitle,
                exception.Message));
    }

    internal static async Task RunSafelyAsync(
        Func<Task> operation,
        Action<Exception> reportException)
    {
        try
        {
            await operation();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            reportException(ex);
        }
    }
}
