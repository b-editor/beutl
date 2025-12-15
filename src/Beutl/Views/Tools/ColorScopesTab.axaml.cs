using Avalonia.Controls;
using Avalonia.Threading;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools.Scopes;

namespace Beutl.Views.Tools;

public partial class ColorScopesTab : UserControl
{
    public ColorScopesTab()
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
