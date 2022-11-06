using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

using Beutl.Api.Objects;

using BeUtl.ViewModels;
using BeUtl.ViewModels.ExtensionsPages.DiscoverPages;

using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Navigation;

namespace BeUtl.Pages.ExtensionsPages.DiscoverPages;

public partial class RankingPage : UserControl
{
    public RankingPage()
    {
        InitializeComponent();
        AddHandler(Frame.NavigatedFromEvent, OnNavigatedFrom, RoutingStrategies.Direct);
        AddHandler(Frame.NavigatedToEvent, OnNavigatedTo, RoutingStrategies.Direct);
    }

    private void OnNavigatedTo(object? sender, NavigationEventArgs e)
    {
        if (e.Parameter is RankingType rankingType)
        {
            DestoryDataContext();
            DataContextFactory factory = GetDataContextFactory();
            DataContext = factory.RankingPage(rankingType);
        }
    }

    private void OnNavigatedFrom(object? sender, NavigationEventArgs e)
    {
        DestoryDataContext();
    }

    private void DestoryDataContext()
    {
        if (DataContext is RankingPageViewModel disposable)
        {
            disposable.Dispose();
        }

        DataContext = null;
    }

    private DataContextFactory GetDataContextFactory()
    {
        return ((ExtensionsPageViewModel)this.FindLogicalAncestorOfType<ExtensionsPage>()!.DataContext!).Discover.DataContextFactory;
    }

    private void Package_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: Package package }
            && this.FindLogicalAncestorOfType<Frame>() is { } frame)
        {
            frame.Navigate(typeof(PublicPackageDetailsPage), package);
        }
        else if (DataContext is RankingPageViewModel viewModel)
        {
            viewModel.More.Execute();
        }
    }
}
