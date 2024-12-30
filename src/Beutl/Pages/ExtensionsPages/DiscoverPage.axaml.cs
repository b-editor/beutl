using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

using Beutl.Api.Objects;

using Beutl.Pages.ExtensionsPages.DiscoverPages;
using Beutl.ViewModels.ExtensionsPages;
using Beutl.ViewModels.ExtensionsPages.DiscoverPages;

using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Navigation;

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
}
