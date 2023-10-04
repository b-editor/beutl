using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

using Beutl.Api.Objects;

using Beutl.ViewModels;
using Beutl.ViewModels.Dialogs;
using Beutl.ViewModels.ExtensionsPages.DevelopPages;
using Beutl.Views.Dialogs;

using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Navigation;

namespace Beutl.Pages.ExtensionsPages.DevelopPages;

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
                Title = Language.ExtensionsPage.DeletePackage_Title,
                Content = Language.ExtensionsPage.DeletePackage_Content,
                PrimaryButtonText = Strings.Yes,
                CloseButtonText = Strings.No,
                DefaultButton = ContentDialogButton.Primary
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                frame.RemoveAllStack(
                    item => (item is Package p1 && p1.Id == viewModel.Package.Id)
                          ||(item is Release p2 && p2.Package.Id == viewModel.Package.Id));

                using (await viewModel.Package.Lock.LockAsync())
                {
                    await viewModel.Package.DeleteAsync();
                }

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
                Title = Language.ExtensionsPage.MakePublicPackage_Title,
                Content = Language.ExtensionsPage.MakePublicPackage_Content,
                PrimaryButtonText = Strings.Yes,
                CloseButtonText = Strings.No,
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
                Title = Language.ExtensionsPage.MakePrivatePackage_Title,
                Content = Language.ExtensionsPage.MakePrivatePackage_Content,
                PrimaryButtonText = Strings.Yes,
                CloseButtonText = Strings.No,
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
