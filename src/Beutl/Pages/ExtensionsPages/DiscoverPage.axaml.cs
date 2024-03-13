using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

using Beutl.Api.Objects;

using Beutl.Pages.ExtensionsPages.DiscoverPages;
using Beutl.ViewModels.ExtensionsPages;
using Beutl.ViewModels.ExtensionsPages.DiscoverPages;

using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Navigation;

using FAHyperlinkButton = FluentAvalonia.UI.Controls.HyperlinkButton;

namespace Beutl.Pages.ExtensionsPages;

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
        if (sender is FAHyperlinkButton { Tag: { } tag }
            && this.FindLogicalAncestorOfType<Frame>() is { } frame)
        {
            RankingType type = tag switch
            {
                "Overall" => RankingType.Overall,
                "Recently" => RankingType.Recently,
                _ => RankingComboBox.SelectedIndex == 0
                    ? RankingType.Daily
                    : RankingType.Weekly,
            };

            frame.Navigate(typeof(RankingPage), type);
        }
    }
}
