using Avalonia.Controls;
using Beutl.ViewModels.Dialogs;

namespace Beutl.Views.Tools;

public partial class AiVideoGenerationView : UserControl
{
    public AiVideoGenerationView()
    {
        InitializeComponent();
        AiPlanReturnRefresh.AttachToDataContext<AiVideoGenerationDialogViewModel>(
            this,
            viewModel => viewModel.AiPlanCoordinator,
            viewModel =>
            {
                viewModel.RefreshAvailability();
                viewModel.RefreshModels();
            });
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);
        (DataContext as AiVideoGenerationDialogViewModel)?.RefreshAvailability();
    }
}
