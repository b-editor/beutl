using Avalonia.Controls;

using Beutl.Editor.Components.NodeTreeTab.ViewModels;

using FluentAvalonia.UI.Controls;

namespace Beutl.Editor.Components.NodeTreeTab.Views;

public partial class NodeTreeTabView : UserControl
{
    public NodeTreeTabView()
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
