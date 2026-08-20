using Avalonia.Controls;
using Beutl.ViewModels.Dialogs;

namespace Beutl.Views.Tools;

public partial class AiImageEditView : UserControl
{
    private IDisposable? _planReturnRefresh;

    public AiImageEditView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _planReturnRefresh?.Dispose();
        _planReturnRefresh = DataContext is AiImageEditDialogViewModel viewModel
            ? AiPlanReturnRefresh.Attach(
                this,
                viewModel.AiPlanCoordinator,
                viewModel.RefreshModels)
            : null;
    }
}
