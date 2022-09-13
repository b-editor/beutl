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
        if (DataContext is PackageDetailsPageViewModel viewModel
            && this.FindAncestorOfType<Frame>() is { } frame)
        {
            PackageSettingsPageViewModel? param = frame.FindParameter<PackageSettingsPageViewModel>(x => x.Package.Id == viewModel.Package.Id);
            param ??= viewModel.CreatePackageSettingsPage();

            frame.Navigate(typeof(PackageSettingsPage), param, SharedNavigationTransitionInfo.Instance);
        }
    }

    private void NavigatePackageReleasesPage_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PackageDetailsPageViewModel viewModel
            && this.FindAncestorOfType<Frame>() is { } frame)
        {
            PackageReleasesPageViewModel? param = frame.FindParameter<PackageReleasesPageViewModel>(x => x.Package.Id == viewModel.Package.Id);
            param ??= viewModel.CreatePackageReleasesPage();

            frame.Navigate(typeof(PackageReleasesPage), param, SharedNavigationTransitionInfo.Instance);
        }
    }
}
