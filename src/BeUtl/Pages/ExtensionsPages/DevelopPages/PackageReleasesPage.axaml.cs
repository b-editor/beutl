using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using BeUtl.ViewModels.ExtensionsPages.DevelopPages;

using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Media.Animation;

namespace BeUtl.Pages.ExtensionsPages.DevelopPages;

public partial class PackageReleasesPage : UserControl
{
    public PackageReleasesPage()
    {
        InitializeComponent();
    }

    private void Add_Click(object? sender, RoutedEventArgs e)
    {

    }

    private void Edit_Click(object? sender, RoutedEventArgs e)
    {

    }
    
    private void Delete_Click(object? sender, RoutedEventArgs e)
    {

    }

    private void NavigatePackageDetailsPage_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PackageReleasesPageViewModel viewModel)
        {
            Frame frame = this.FindAncestorOfType<Frame>();
            var transitionInfo = new EntranceNavigationTransitionInfo
            {
                FromHorizontalOffset = -28,
                FromVerticalOffset = 0
            };

            frame.Navigate(typeof(PackageDetailsPage), viewModel.Parent, transitionInfo);
        }
    }
}
