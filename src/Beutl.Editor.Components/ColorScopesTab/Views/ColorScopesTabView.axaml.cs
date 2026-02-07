using Avalonia.Controls;
using Avalonia.Threading;

using Beutl.Editor.Components.ColorScopesTab.ViewModels;

namespace Beutl.Editor.Components.ColorScopesTab.Views;

public partial class ColorScopesTabView : UserControl
{
    public ColorScopesTabView()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is ColorScopesTabViewModel viewModel)
        {
            RefreshCurrentScope();
            viewModel.RefreshRequested += OnRefreshRequested;
        }
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
        }
    }
}
