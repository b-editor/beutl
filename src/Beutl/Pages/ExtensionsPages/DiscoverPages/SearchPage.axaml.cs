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
    }

    private void OnNavigatedTo(object? sender, FANavigationEventArgs e)
    {
        if (e.Parameter is string keyword)
        {
            DestroyDataContext();
            DataContextFactory factory = GetDataContextFactory();
            DataContext = factory.SearchPage(keyword);
        }
    }

    private void OnNavigatedFrom(object? sender, FANavigationEventArgs e)
    {
        DestroyDataContext();
    }

    private void DestroyDataContext()
    {
        DiscoverPageHelper.DestroyDataContext<SearchPageViewModel>(this);
    }

    private DataContextFactory GetDataContextFactory()
    {
        return DiscoverPageHelper.GetDataContextFactory(this);
    }

    private void Package_Click(object? sender, RoutedEventArgs e)
    {
        if (DiscoverPageHelper.TryNavigateToPackage(this, sender))
        {
            return;
        }

        if (DataContext is SearchPageViewModel viewModel)
        {
            viewModel.More.Execute();
        }
    }
}
