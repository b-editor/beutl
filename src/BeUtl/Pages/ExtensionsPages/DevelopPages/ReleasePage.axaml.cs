using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

using Beutl.Api.Objects;

using BeUtl.ViewModels;
using BeUtl.ViewModels.Dialogs;
using BeUtl.ViewModels.ExtensionsPages.DevelopPages;
using BeUtl.Views.Dialogs;

using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Navigation;

namespace BeUtl.Pages.ExtensionsPages.DevelopPages;

public sealed partial class ReleasePage : UserControl
{
    public ReleasePage()
    {
        InitializeComponent();
        AddHandler(Frame.NavigatedFromEvent, OnNavigatedFrom, RoutingStrategies.Direct);
        AddHandler(Frame.NavigatedToEvent, OnNavigatedTo, RoutingStrategies.Direct);
    }

    private void OnNavigatedTo(object? sender, NavigationEventArgs e)
    {
        if (e.Parameter is Release release)
        {
            DestoryDataContext();
            DataContextFactory factory = GetDataContextFactory();
            DataContext = factory.ReleasePage(release);
        }
    }

    private void OnNavigatedFrom(object? sender, NavigationEventArgs e)
    {
        DestoryDataContext();
    }

    private void DestoryDataContext()
    {
        if (DataContext is ReleasePageViewModel disposable)
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
        if (DataContext is ReleasePageViewModel viewModel
            && this.FindAncestorOfType<Frame>() is { } frame)
        {
            frame.Navigate(typeof(PackageDetailsPage), viewModel.Release.Package, SharedNavigationTransitionInfo.Instance);
        }
    }

    private void NavigatePackageReleasesPage_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ReleasePageViewModel viewModel
            && this.FindAncestorOfType<Frame>() is { } frame)
        {
            frame.Navigate(typeof(PackageReleasesPage), viewModel.Release.Package, SharedNavigationTransitionInfo.Instance);
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
                var releases = frame.FindParameter<PackageReleasesPageViewModel>(x => x.Package.Id == viewModel.Release.Package.Id);
                if (releases != null)
                {
                    releases.Items.Remove(viewModel.Release);
                }

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
            SelectAssetViewModel dialogViewModel = viewModel.SelectReleaseAsset();
            var dialog = new SelectAsset
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
