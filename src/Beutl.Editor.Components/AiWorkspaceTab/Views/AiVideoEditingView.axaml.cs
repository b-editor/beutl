using Avalonia.Controls;
using Beutl.Editor.Components.AiWorkspaceTab.Services;
using Beutl.Editor.Components.AiWorkspaceTab.ViewModels;

namespace Beutl.Editor.Components.AiWorkspaceTab.Views;

public partial class AiVideoEditingView : UserControl
{
    public AiVideoEditingView()
    {
        InitializeComponent();
        AiPlanReturnRefresh.AttachToDataContext<AiVideoEditingViewModel>(
            this,
            group => group.ActiveContent.Value?.AiPlanCoordinator,
            group =>
            {
                group.ActiveContent.Value?.RefreshAvailability();
                group.RefreshModels();
            });
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);
        (DataContext as AiVideoEditingViewModel)?.ActiveContent.Value?.RefreshAvailability();
    }
}
