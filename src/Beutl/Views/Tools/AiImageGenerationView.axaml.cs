using Avalonia.Controls;
using Beutl.ViewModels.Dialogs;

namespace Beutl.Views.Tools;

public partial class AiImageGenerationView : UserControl
{
    public AiImageGenerationView()
    {
        InitializeComponent();
        AiPlanReturnRefresh.AttachToDataContext<AiImageGenerationDialogViewModel>(
            this, viewModel => viewModel.AiPlanCoordinator, viewModel => viewModel.RefreshModels());
    }
}
