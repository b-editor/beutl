using Avalonia.Controls;
using Beutl.Editor.Components.AiWorkspaceTab.Services;
using Beutl.Editor.Components.AiWorkspaceTab.ViewModels;

namespace Beutl.Editor.Components.AiWorkspaceTab.Views;

public partial class AiWorkspaceView : UserControl
{
    private IDisposable? _planReturnRefresh;

    public AiWorkspaceView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _planReturnRefresh?.Dispose();
        // Generation pages refresh their own models on the same event, so the workspace does that only for
        // Jobs; otherwise a purchase made while Jobs is shown leaves the Join Pro gate up.
        _planReturnRefresh = DataContext is AiWorkspaceViewModel viewModel
            && viewModel.AiPlanCoordinator is { } coordinator
            ? AiPlanReturnRefresh.Attach(
                this,
                coordinator,
                () => viewModel.NotifyPlanReturnRefreshed(
                    refreshModels: viewModel.ActiveContent.Value is not IAiModelListConsumer))
            : null;
    }
}
