using Avalonia.Controls;
using Beutl.ViewModels.Dialogs;
using Beutl.ViewModels.Tools;

namespace Beutl.Views.Tools;

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
        // The plan is bought on the website, so re-read entitlements when the app
        // is focused again. Generation pages already refresh their own models on
        // the same event, so the workspace only does that for Jobs. Without this
        // a purchase made while Jobs is selected leaves the Join Pro gate up
        // until another activation happens.
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
