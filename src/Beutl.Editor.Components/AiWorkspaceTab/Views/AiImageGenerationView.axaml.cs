using Avalonia.Controls;
using Beutl.Editor.Components.AiWorkspaceTab.Services;
using Beutl.Editor.Components.AiWorkspaceTab.ViewModels;

namespace Beutl.Editor.Components.AiWorkspaceTab.Views;

public partial class AiImageGenerationView : UserControl
{
    public AiImageGenerationView()
    {
        InitializeComponent();
        AiPlanReturnRefresh.AttachToDataContext<AiImageGenerationDialogViewModel>(
            this, viewModel => viewModel.AiPlanCoordinator, viewModel => viewModel.RefreshModels());
    }
}
