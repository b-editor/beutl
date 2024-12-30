using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Styling;

using Beutl.Api.Objects;

using Beutl.ViewModels.ExtensionsPages.DiscoverPages;

using FluentAvalonia.Styling;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Navigation;

namespace Beutl.Pages.ExtensionsPages.DiscoverPages;

public partial class UserProfilePage : UserControl
{
    public UserProfilePage()
    {
        InitializeComponent();
        AddHandler(Frame.NavigatedFromEvent, OnNavigatedFrom, RoutingStrategies.Direct);
        AddHandler(Frame.NavigatedToEvent, OnNavigatedTo, RoutingStrategies.Direct);
    }

    private void OnNavigatedTo(object? sender, NavigationEventArgs e)
    {
        if (e.Parameter is Profile user)
        {
            DestoryDataContext();
            DataContext = new UserProfilePageViewModel(user);
        }
    }

    private void OnNavigatedFrom(object? sender, NavigationEventArgs e)
    {
        DestoryDataContext();
    }

    private void DestoryDataContext()
    {
        if (DataContext is UserProfilePageViewModel disposable)
        {
            disposable.Dispose();
        }

        DataContext = null;
    }

    private void Package_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: Package package }
            && this.FindLogicalAncestorOfType<Frame>() is { } frame)
        {
            frame.Navigate(typeof(PublicPackageDetailsPage), package);
        }
        else if (DataContext is UserProfilePageViewModel viewModel)
        {
            viewModel.More.Execute();
        }
    }
}
