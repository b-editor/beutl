using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;

using Beutl.Api.Objects;

using BeUtl.ViewModels;
using BeUtl.ViewModels.Dialogs;
using BeUtl.ViewModels.ExtensionsPages.DevelopPages;
using BeUtl.Views.Dialogs;

using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Navigation;

namespace BeUtl.Pages.ExtensionsPages.DevelopPages;

public sealed partial class PackageSettingsPage : UserControl
{
    public PackageSettingsPage()
    {
        InitializeComponent();
        AddHandler(Frame.NavigatedFromEvent, OnNavigatedFrom, RoutingStrategies.Direct);
        AddHandler(Frame.NavigatedToEvent, OnNavigatedTo, RoutingStrategies.Direct);
    }

    private void OnNavigatedTo(object? sender, NavigationEventArgs e)
    {
        if (e.Parameter is Package package)
        {
            DestoryDataContext();
            DataContextFactory factory = GetDataContextFactory();
            DataContext = factory.PackageSettingsPage(package);
        }
    }

    private void OnNavigatedFrom(object? sender, NavigationEventArgs e)
    {
        DestoryDataContext();
    }

    private void DestoryDataContext()
    {
        if (DataContext is PackageSettingsPageViewModel disposable)
        {
            disposable.Dispose();
        }

        DataContext = null;
    }

    private DataContextFactory GetDataContextFactory()
    {
        return ((ExtensionsPageViewModel)this.FindLogicalAncestorOfType<ExtensionsPage>()!.DataContext!).Develop.DataContextFactory;
    }

    private void NavigatePackageDetailsPage_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PackageSettingsPageViewModel viewModel
            && this.FindAncestorOfType<Frame>() is { } frame)
        {
            frame.Navigate(typeof(PackageDetailsPage), viewModel.Package, SharedNavigationTransitionInfo.Instance);
        }
    }

    private async void DeletePackage_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PackageSettingsPageViewModel viewModel
            && this.FindAncestorOfType<Frame>() is { } frame)
        {
            var dialog = new ContentDialog
            {
                Title = S.DevelopPage.DeletePackage.Title,
                Content = S.DevelopPage.DeletePackage.Content,
                PrimaryButtonText = S.Common.Yes,
                CloseButtonText = S.Common.No,
                DefaultButton = ContentDialogButton.Primary
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                frame.RemoveAllStack(
                    item => (item is PackageDetailsPageViewModel p1 && p1.Package.Id == viewModel.Package.Id)
                         || (item is PackageSettingsPageViewModel p2 && p2.Package.Id == viewModel.Package.Id)
                         || (item is ReleasePageViewModel p4 && p4.Release.Package.Id == viewModel.Package.Id)
                         || (item is PackageReleasesPageViewModel p5 && p5.Package.Id == viewModel.Package.Id));

                await viewModel.Package.DeleteAsync();

                frame.GoBack();
            }
        }
    }

    private async void MakePublic_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PackageSettingsPageViewModel viewModel)
        {
            var dialog = new ContentDialog
            {
                Title = S.DevelopPage.MakePublicPackage.Title,
                Content = S.DevelopPage.MakePublicPackage.Content,
                PrimaryButtonText = S.Common.Yes,
                CloseButtonText = S.Common.No,
                DefaultButton = ContentDialogButton.Primary
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                viewModel.MakePublic.Execute();
            }
        }
    }

    private async void MakePrivate_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PackageSettingsPageViewModel viewModel)
        {
            var dialog = new ContentDialog
            {
                Title = S.DevelopPage.MakePrivatePackage.Title,
                Content = S.DevelopPage.MakePrivatePackage.Content,
                PrimaryButtonText = S.Common.Yes,
                CloseButtonText = S.Common.No,
                DefaultButton = ContentDialogButton.Primary
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                viewModel.MakePrivate.Execute();
            }
        }
    }

    private async void OpenLogoFile_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PackageSettingsPageViewModel viewModel)
        {
            SelectImageAssetViewModel dialogViewModel = viewModel.SelectImageAssetViewModel();
            var dialog = new SelectImageAsset
            {
                DataContext = dialogViewModel
            };

            await dialog.ShowAsync();

            if (dialogViewModel.SelectedItem.Value is { } selectedItem)
            {
                viewModel.Logo.Value = selectedItem;
            }
        }
    }

    private void RemoveLogo_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PackageSettingsPageViewModel viewModel)
        {
            viewModel.Logo.Value = null;
        }
    }

    private async void AddScreenshotFile_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PackageSettingsPageViewModel viewModel)
        {
            SelectImageAssetViewModel dialogViewModel = viewModel.SelectImageAssetViewModel();
            var dialog = new SelectImageAsset
            {
                DataContext = dialogViewModel
            };

            await dialog.ShowAsync();

            if (dialogViewModel.SelectedItem.Value is { } selectedItem)
            {
                viewModel.AddScreenshot.Execute(selectedItem);
            }
        }
    }
}
