using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Beutl.Agents;
using Beutl.Editor.Components.AgentTab.ViewModels;

namespace Beutl.Editor.Components.AgentTab.Views;

internal sealed partial class AgentThreadBar : UserControl
{
    private AgentThreadMetadata? _actionThread;
    private Flyout ThreadPicker => (Flyout)ThreadSwitcherButton.Flyout!;
    private Flyout ThreadActionFlyout => (Flyout)FlyoutBase.GetAttachedFlyout(ThreadSwitcherButton)!;

    public AgentThreadBar()
    {
        InitializeComponent();
    }

    private void OnThreadPickerOpening(object? sender, EventArgs e)
    {
        if (DataContext is AgentTabViewModel viewModel)
            viewModel.ThreadSearch = null;
    }

    private async void OnThreadPickerOpened(object? sender, EventArgs e)
    {
        ThreadSearchBox.Focus();
        if (DataContext is AgentTabViewModel viewModel)
            await viewModel.RefreshThreadsAsync();
    }

    private async void OnSelectThread(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AgentThreadRow row } && DataContext is AgentTabViewModel viewModel)
        {
            ThreadPicker.Hide();
            if (row.Thread.Id != viewModel.SelectedThread?.Id)
                await viewModel.SelectThreadAsync(row.Thread);
        }
    }

    private void OnBeginRename(object? sender, RoutedEventArgs e) => ShowThreadAction(sender, rename: true);

    private void OnBeginDelete(object? sender, RoutedEventArgs e) => ShowThreadAction(sender, rename: false);

    private void ShowThreadAction(object? sender, bool rename)
    {
        if (DataContext is not AgentTabViewModel { CanManage: true } viewModel)
            return;

        var thread = (sender as Control)?.DataContext is AgentThreadRow row ? row.Thread : viewModel.SelectedThread;
        if (thread is null)
            return;

        ThreadPicker.Hide();
        _actionThread = thread;
        RenameForm.IsVisible = rename;
        DeleteForm.IsVisible = !rename;
        RenameTextBox.Text = thread.Title;
        DeleteTitle.Text = thread.Title;
        ThreadActionFlyout.ShowAt(ThreadSwitcherButton);
    }

    private void OnThreadActionOpened(object? sender, EventArgs e)
    {
        if (RenameForm.IsVisible)
        {
            RenameTextBox.Focus();
            RenameTextBox.SelectAll();
        }
    }

    private void OnThreadActionClosed(object? sender, EventArgs e) => _actionThread = null;

    private void OnCancelThreadAction(object? sender, RoutedEventArgs e) => ThreadActionFlyout.Hide();

    private async void OnCommitThreadAction(object? sender, RoutedEventArgs e)
    {
        if (_actionThread is not { } thread || DataContext is not AgentTabViewModel viewModel)
            return;

        bool rename = RenameForm.IsVisible;
        string? name = RenameTextBox.Text;
        if (rename && string.IsNullOrWhiteSpace(name))
            return;

        ThreadActionFlyout.Hide();
        if (rename)
            await viewModel.RenameThreadAsync(thread, name!);
        else
            await viewModel.DeleteThreadAsync(thread);
    }
}
