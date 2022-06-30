using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using BeUtl.ViewModels.ExtensionsPages.DevelopPages;

using FluentAvalonia.UI.Controls;

namespace BeUtl.Pages.ExtensionsPages.DevelopPages;

public sealed partial class PackageDetailsPage : UserControl
{
    public PackageDetailsPage()
    {
        InitializeComponent();
    }

    private void NavigatePackageSettingsPage_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PackageDetailsPageViewModel viewModel)
        {
            Frame frame = this.FindAncestorOfType<Frame>();
            frame.Navigate(typeof(PackageSettingsPage), viewModel.Settings, SharedNavigationTransitionInfo.Instance);
        }
    }

    private void NavigatePackageReleasesPage_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PackageDetailsPageViewModel viewModel)
        {
            Frame frame = this.FindAncestorOfType<Frame>();
            frame.Navigate(typeof(PackageReleasesPage), viewModel.Releases, SharedNavigationTransitionInfo.Instance);
        }
    }
}
