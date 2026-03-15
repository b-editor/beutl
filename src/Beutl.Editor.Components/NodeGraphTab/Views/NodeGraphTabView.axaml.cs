using Avalonia.Controls;

using Beutl.Editor.Components.NodeGraphTab.ViewModels;

using FluentAvalonia.UI.Controls;

namespace Beutl.Editor.Components.NodeGraphTab.Views;

public partial class NodeGraphTabView : UserControl
{
    public NodeGraphTabView()
    {
        InitializeComponent();
    }

    private void BreadcrumbBarItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        if (DataContext is NodeGraphTabViewModel viewModel)
        {
            viewModel.NavigateTo(args.Index);
        }
    }
}
