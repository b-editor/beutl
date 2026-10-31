using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.Pages.ExtensionsPages.DiscoverPages;
using Beutl.ViewModels.ExtensionsPages;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Navigation;

namespace Beutl.Pages.ExtensionsPages;

public sealed partial class DiscoverPage : UserControl
{
    public DiscoverPage()
    {
        InitializeComponent();
        AddHandler(FAFrame.NavigatedToEvent, OnNavigatedTo, RoutingStrategies.Direct);
        AddHandler(Button.ClickEvent, OnPackageCardClick);
    }

    private void OnNavigatedTo(object? sender, FANavigationEventArgs e)
    {
        if (e.Parameter is DiscoverPageViewModel viewModel)
        {
            DataContext = viewModel;
        }
    }

    private void OnPackageCardClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DiscoverPageViewModel viewModel)
        {
            DiscoverPageHelper.OnPackageCardClick(this, e, viewModel.More);
        }
    }
}
