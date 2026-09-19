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
        // is focused again. Without this a purchase made while the Jobs section is
        // selected leaves the Join Pro gate up until another activation happens.
        _planReturnRefresh = DataContext is AiWorkspaceViewModel viewModel
            && viewModel.AiPlanCoordinator is { } coordinator
            ? AiPlanReturnRefresh.Attach(
                this,
                coordinator,
                () => (viewModel.ActiveContent.Value as IAiModelListConsumer)?.RefreshModels())
            : null;
    }
}
