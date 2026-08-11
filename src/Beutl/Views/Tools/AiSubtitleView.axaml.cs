using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.ViewModels.Dialogs;

namespace Beutl.Views.Tools;

public partial class AiSubtitleView : UserControl
{
    private IDisposable? _planReturnRefresh;

    public AiSubtitleView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _planReturnRefresh?.Dispose();
        _planReturnRefresh = DataContext is AiSubtitleDialogViewModel viewModel
            ? AiPlanReturnRefresh.Attach(this, viewModel.AiPlanCoordinator)
            : null;
    }

    private void OnConfirmHistoryOverwriteClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AiSubtitleDialogViewModel viewModel)
        {
            viewModel.ConfirmPendingHistoryResult();
        }
    }

    private void OnCancelHistoryOverwriteClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AiSubtitleDialogViewModel viewModel)
        {
            viewModel.DiscardPendingHistoryResult();
        }
    }
}
