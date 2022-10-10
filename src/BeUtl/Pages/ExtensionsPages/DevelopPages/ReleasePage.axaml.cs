using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using BeUtl.Pages.ExtensionsPages.DevelopPages.Dialogs;
using BeUtl.ViewModels.ExtensionsPages.DevelopPages;
using BeUtl.ViewModels.ExtensionsPages.DevelopPages.Dialogs;

using FluentAvalonia.UI.Controls;

namespace BeUtl.Pages.ExtensionsPages.DevelopPages;

public sealed partial class ReleasePage : UserControl
{
    public ReleasePage()
    {
        InitializeComponent();
    }

    private void NavigatePackageDetailsPage_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ReleasePageViewModel viewModel
            && this.FindAncestorOfType<Frame>() is { } frame)
        {
            PackageDetailsPageViewModel? param = frame.FindParameter<PackageDetailsPageViewModel>(x => x.Package.Id == viewModel.Release.Package.Id);
            param ??= viewModel.CreatePackageDetailsPage();

            frame.Navigate(typeof(PackageDetailsPage), param, SharedNavigationTransitionInfo.Instance);
        }
    }

    private void NavigatePackageReleasesPage_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ReleasePageViewModel viewModel
            && this.FindAncestorOfType<Frame>() is { } frame)
        {
            PackageReleasesPageViewModel? param = frame.FindParameter<PackageReleasesPageViewModel>(x => x.Package.Id == viewModel.Release.Package.Id);
            param ??= viewModel.CreatePackageReleasesPage();

            frame.Navigate(typeof(PackageReleasesPage), param, SharedNavigationTransitionInfo.Instance);
        }
    }

    private async void DeleteRelease_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ReleasePageViewModel viewModel
            && this.FindAncestorOfType<Frame>() is { } frame)
        {
            var dialog = new ContentDialog
            {
                Title = S.DevelopPage.DeleteRelease.Title,
                Content = S.DevelopPage.DeleteRelease.Content,
                PrimaryButtonText = S.Common.Yes,
                CloseButtonText = S.Common.No,
                DefaultButton = ContentDialogButton.Primary
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                long releaseId = viewModel.Release.Id;
                frame.RemoveAllStack(item => item is ReleasePageViewModel p && p.Release.Id == releaseId);

                viewModel.Delete.Execute();
                frame.GoBack();
            }
        }
    }

    private async void MakePublic_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ReleasePageViewModel viewModel)
        {
            var dialog = new ContentDialog
            {
                Title = S.DevelopPage.MakePublicRelease.Title,
                Content = S.DevelopPage.MakePublicRelease.Content,
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
        if (DataContext is ReleasePageViewModel viewModel)
        {
            var dialog = new ContentDialog
            {
                Title = S.DevelopPage.MakePrivateRelease.Title,
                Content = S.DevelopPage.MakePrivateRelease.Content,
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

    private async void SelectAsset_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ReleasePageViewModel viewModel)
        {
            SelectReleaseAssetViewModel dialogViewModel = viewModel.CreateSelectReleaseAsset();
            var dialog = new SelectReleaseAsset
            {
                DataContext = dialogViewModel
            };

            await dialog.ShowAsync();

            if (dialogViewModel.SelectedItem.Value is { } selectedItem)
            {
                viewModel.Asset.Value = selectedItem.Model;
            }
        }
    }
}
