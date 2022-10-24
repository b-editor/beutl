using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;

using Beutl.Api.Objects;

using BeUtl.Pages.ExtensionsPages.DiscoverPages;
using BeUtl.ViewModels.ExtensionsPages;
using BeUtl.ViewModels.ExtensionsPages.DiscoverPages;

using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Navigation;

namespace BeUtl.Pages.ExtensionsPages;

public sealed partial class DiscoverPage : UserControl
{
    public DiscoverPage()
    {
        InitializeComponent();
        AddHandler(Frame.NavigatedToEvent, OnNavigatedTo, RoutingStrategies.Direct);
    }

    private void OnNavigatedTo(object? sender, NavigationEventArgs e)
    {
        if (e.Parameter is DiscoverPageViewModel viewModel)
        {
            DataContext = viewModel;
        }
    }

    private void Package_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: Package package }
            && this.FindLogicalAncestorOfType<Frame>() is { } frame)
        {
            frame.Navigate(typeof(PublicPackageDetailsPage), package);
        }
    }

    private void MoreRanking_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is HyperlinkButton { Tag: { } tag }
            && this.FindLogicalAncestorOfType<Frame>() is { } frame)
        {
            RankingPageViewModel.RankingType type = tag switch
            {
                "Overall" => RankingPageViewModel.RankingType.Overall,
                "Recently" => RankingPageViewModel.RankingType.Recently,
                _ => RankingComboBox.SelectedIndex == 0
                    ? RankingPageViewModel.RankingType.Daily
                    : RankingPageViewModel.RankingType.Weekly,
            };

            frame.Navigate(typeof(RankingPage), type);
        }
    }
}
