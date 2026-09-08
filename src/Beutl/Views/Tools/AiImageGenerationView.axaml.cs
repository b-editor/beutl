using Avalonia.Controls;
using Beutl.ViewModels.Dialogs;

namespace Beutl.Views.Tools;

public partial class AiImageGenerationView : UserControl
{
    private IDisposable? _planReturnRefresh;

    public AiImageGenerationView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _planReturnRefresh?.Dispose();
        _planReturnRefresh = DataContext is AiImageGenerationDialogViewModel viewModel
            ? AiPlanReturnRefresh.Attach(
                this,
                viewModel.AiPlanCoordinator,
                viewModel.RefreshModels)
            : null;
    }
}
