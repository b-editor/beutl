using Avalonia.Controls;

using Beutl.ViewModels.NodeTree;

using FluentAvalonia.UI.Controls;

namespace Beutl.Views.NodeTree;

public partial class NodeTreeTab : UserControl
{
    public NodeTreeTab()
    {
        InitializeComponent();
    }

    private void BreadcrumbBarItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        if (DataContext is NodeTreeTabViewModel viewModel)
        {
            viewModel.NavigateTo(args.Index);
        }
    }
}
