using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.ViewModels;

namespace Beutl.Views;

public sealed partial class TitleBarStatusView : UserControl
{
    private TitleBarStatusViewModel? _openedViewModel;

    public TitleBarStatusView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        StatusButton.Flyout?.Hide();
        OnFlyoutClosed(this, EventArgs.Empty);
    }

    private void OnFlyoutOpening(object? sender, EventArgs e)
    {
        if (DataContext is TitleBarStatusViewModel viewModel)
        {
            _openedViewModel = viewModel;
            viewModel.OnPopupOpened();
        }
    }

    private void OnFlyoutClosed(object? sender, EventArgs e)
    {
        _openedViewModel?.OnPopupClosed();
        _openedViewModel = null;
    }

    private void OnOpenAiJobsClick(object? sender, RoutedEventArgs e)
    {
        StatusButton.Flyout?.Hide();
        if (DataContext is TitleBarStatusViewModel viewModel)
        {
            viewModel.OpenAiJobCenter();
        }
    }
}
