using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.Api.Objects;

using Beutl.ViewModels.ExtensionsPages.DiscoverPages;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Navigation;

namespace Beutl.Pages.ExtensionsPages.DiscoverPages;

public partial class UserProfilePage : UserControl
{
    public UserProfilePage()
    {
        InitializeComponent();
        AddHandler(FAFrame.NavigatedFromEvent, OnNavigatedFrom, RoutingStrategies.Direct);
        AddHandler(FAFrame.NavigatedToEvent, OnNavigatedTo, RoutingStrategies.Direct);
    }

    private void OnNavigatedTo(object? sender, FANavigationEventArgs e)
    {
        if (e.Parameter is Profile user)
        {
            DiscoverPageHelper.DestroyDataContext<UserProfilePageViewModel>(this);
            DataContext = new UserProfilePageViewModel(user);
        }
    }

    private void OnNavigatedFrom(object? sender, FANavigationEventArgs e)
    {
        DiscoverPageHelper.DestroyDataContext<UserProfilePageViewModel>(this);
    }

    private void Package_Click(object? sender, RoutedEventArgs e)
    {
        if (DiscoverPageHelper.TryNavigateToPackage(this, sender))
        {
            return;
        }

        if (DataContext is UserProfilePageViewModel viewModel)
        {
            viewModel.More.Execute();
        }
    }
}
