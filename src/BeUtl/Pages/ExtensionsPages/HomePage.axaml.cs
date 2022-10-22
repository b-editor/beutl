using Avalonia.Controls;
using Avalonia.Interactivity;

using BeUtl.ViewModels.ExtensionsPages;

using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Navigation;

namespace BeUtl.Pages.ExtensionsPages;
public sealed partial class HomePage : UserControl
{
    public HomePage()
    {
        InitializeComponent();
        AddHandler(Frame.NavigatedToEvent, OnNavigatedTo, RoutingStrategies.Direct);
    }

    private void OnNavigatedTo(object? sender, NavigationEventArgs e)
    {
        if (e.Parameter is HomePageViewModel viewModel)
        {
            DataContext = viewModel;
        }
    }
}
