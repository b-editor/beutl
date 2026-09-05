using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.ViewModels.Tools;

namespace Beutl.Views.Tools;

public partial class AiJobCenterView : UserControl
{
    public AiJobCenterView()
    {
        InitializeComponent();
    }

    private void OnJobCardLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AiJobCenterViewModel viewModel
            && sender is AiJobCard { DataContext: AiJobItemViewModel item })
        {
            viewModel.SetPreviewVisibility(item, true);
        }
    }

    private void OnJobCardUnloaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AiJobCenterViewModel viewModel
            && sender is AiJobCard { DataContext: AiJobItemViewModel item })
        {
            viewModel.SetPreviewVisibility(item, false);
        }
    }

    private async void OnAddToSceneClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AiJobCenterViewModel viewModel
            && sender is Button { Tag: AiJobItemViewModel { CanAddToScene: true } item })
        {
            await viewModel.AddToSceneAsync(item);
        }
    }

    private async void OnRetryClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AiJobCenterViewModel viewModel
            || sender is not Button { Tag: AiJobItemViewModel { CanRetry: true } item })
        {
            return;
        }

        await viewModel.RequestRetryConfirmationAsync(item);
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AiJobCenterViewModel viewModel
            || sender is not Button { Tag: AiJobItemViewModel { CanDelete: true } item })
        {
            return;
        }

        viewModel.RequestDeleteConfirmation(item);
    }

    private async void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AiJobCenterViewModel viewModel)
        {
            await viewModel.ConfirmPendingActionAsync();
        }
    }

    private void OnCancelConfirmationClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AiJobCenterViewModel viewModel)
        {
            viewModel.CancelConfirmation();
        }
    }
}

internal sealed class AiJobCard : Border
{
    protected override AutomationPeer OnCreateAutomationPeer()
        => new AiJobCardAutomationPeer(this);

    private sealed class AiJobCardAutomationPeer(AiJobCard owner) : ControlAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore()
            => AutomationControlType.ListItem;
    }
}
