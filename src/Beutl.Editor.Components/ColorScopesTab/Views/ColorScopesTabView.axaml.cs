using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Beutl.Editor.Components.ColorScopesTab.ViewModels;

namespace Beutl.Editor.Components.ColorScopesTab.Views;

public partial class ColorScopesTabView : UserControl
{
    private ColorScopesTabViewModel? _subscribedViewModel;

    public ColorScopesTabView()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SubscribeRefresh(DataContext as ColorScopesTabViewModel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        SubscribeRefresh(null);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is ColorScopesTabViewModel)
        {
            RefreshCurrentScope();
        }

        if (this.IsAttachedToVisualTree())
        {
            SubscribeRefresh(DataContext as ColorScopesTabViewModel);
        }
    }

    // Only the current view model reaches the view, and only while it is attached.
    private void SubscribeRefresh(ColorScopesTabViewModel? viewModel)
    {
        if (ReferenceEquals(_subscribedViewModel, viewModel))
            return;

        if (_subscribedViewModel != null)
            _subscribedViewModel.RefreshRequested -= OnRefreshRequested;

        _subscribedViewModel = viewModel;

        if (viewModel != null)
            viewModel.RefreshRequested += OnRefreshRequested;
    }

    private void OnRefreshRequested(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(RefreshCurrentScope);
    }

    private void RefreshCurrentScope()
    {
        if (DataContext is not ColorScopesTabViewModel viewModel)
            return;

        switch (viewModel.SelectedScopeType.Value)
        {
            case ColorScopeType.Waveform:
                WaveformControl?.Refresh();
                break;
            case ColorScopeType.Histogram:
                HistogramControl?.Refresh();
                break;
            case ColorScopeType.Vectorscope:
                VectorscopeControl?.Refresh();
                break;
            case ColorScopeType.FalseColor:
                FalseColorControl?.Refresh();
                break;
            case ColorScopeType.Zebra:
                ZebraControl?.Refresh();
                break;
        }
    }
}
