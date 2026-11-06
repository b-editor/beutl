using Avalonia.Controls;
using Avalonia.Interactivity;
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
    }

    private void OnNavigatedTo(object? sender, FANavigationEventArgs e)
    {
        if (e.Parameter is DiscoverPageViewModel viewModel)
        {
            DataContext = viewModel;
        }
    }
}
