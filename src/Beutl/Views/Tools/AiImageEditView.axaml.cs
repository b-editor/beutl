using Avalonia.Controls;
using Beutl.ViewModels.Dialogs;

namespace Beutl.Views.Tools;

public partial class AiImageEditView : UserControl
{
    public AiImageEditView()
    {
        InitializeComponent();
        AiPlanReturnRefresh.AttachToDataContext<AiImageEditDialogViewModel>(
            this, viewModel => viewModel.AiPlanCoordinator, viewModel => viewModel.RefreshModels());
    }
}
