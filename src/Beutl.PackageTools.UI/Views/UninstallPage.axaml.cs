using Beutl.PackageTools.UI.ViewModels;

namespace Beutl.PackageTools.UI.Views;


public partial class UninstallPage : PackageToolPage
{
    public UninstallPage()
    {
        InitializeComponent();
        PackageActionNavigation.HandleNavigatedTo<UninstallViewModel>(
            this,
            Scroll,
            (viewModel, token) => Task.Run(() => viewModel.Run(token)));
    }
}
