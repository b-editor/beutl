using Avalonia.Controls;
using Beutl.Editor.Components.AiWorkspaceTab.Services;
using Beutl.Editor.Components.AiWorkspaceTab.ViewModels;

namespace Beutl.Editor.Components.AiWorkspaceTab.Views;

public partial class AiImageEditView : UserControl
{
    public AiImageEditView()
    {
        InitializeComponent();
        AiPlanReturnRefresh.AttachToDataContext<AiImageEditDialogViewModel>(
            this, viewModel => viewModel.AiPlanCoordinator, viewModel => viewModel.RefreshModels());
    }
}
