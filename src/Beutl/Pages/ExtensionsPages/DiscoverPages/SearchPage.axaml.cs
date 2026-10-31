using Avalonia.Controls;
using Avalonia.Interactivity;

using Beutl.ViewModels.ExtensionsPages.DiscoverPages;

using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Navigation;

namespace Beutl.Pages.ExtensionsPages.DiscoverPages;

public partial class SearchPage : UserControl
{
    public SearchPage()
    {
        InitializeComponent();
        AddHandler(FAFrame.NavigatedFromEvent, OnNavigatedFrom, RoutingStrategies.Direct);
        AddHandler(FAFrame.NavigatedToEvent, OnNavigatedTo, RoutingStrategies.Direct);
        AddHandler(Button.ClickEvent, OnPackageCardClick);
    }

    private void OnNavigatedTo(object? sender, FANavigationEventArgs e)
    {
        if (e.Parameter is string keyword)
        {
            DiscoverPageHelper.DestroyDataContext<SearchPageViewModel>(this);
            DataContextFactory factory = DiscoverPageHelper.GetDataContextFactory(this);
            DataContext = factory.SearchPage(keyword);
        }
    }

    private void OnNavigatedFrom(object? sender, FANavigationEventArgs e)
    {
        DiscoverPageHelper.DestroyDataContext<SearchPageViewModel>(this);
    }

    private void OnPackageCardClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SearchPageViewModel viewModel)
        {
            DiscoverPageHelper.OnPackageCardClick(this, e, viewModel.More);
        }
    }
}
