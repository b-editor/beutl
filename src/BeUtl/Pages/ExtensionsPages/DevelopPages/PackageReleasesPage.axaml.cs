using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

using Beutl.Api.Objects;

using BeUtl.Pages.ExtensionsPages.DevelopPages.Dialogs;
using BeUtl.ViewModels.ExtensionsPages.DevelopPages;
using BeUtl.ViewModels.ExtensionsPages.DevelopPages.Dialogs;

using FluentAvalonia.UI.Controls;

namespace BeUtl.Pages.ExtensionsPages.DevelopPages;

public sealed partial class PackageReleasesPage : UserControl
{
    private bool _flag;

    public PackageReleasesPage()
    {
        InitializeComponent();
        ReleasesList.AddHandler(PointerPressedEvent, ReleasesList_PointerPressed, RoutingStrategies.Tunnel);
        ReleasesList.AddHandler(PointerReleasedEvent, ReleasesList_PointerReleased, RoutingStrategies.Tunnel);
    }

    private void ReleasesList_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_flag)
        {
            if (ReleasesList.SelectedItem is Release item)
            {
                NavigateToReleasePage(item);
            }

            _flag = false;
        }
    }

    private void ReleasesList_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _flag = true;
        }
    }

    private async void Add_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PackageReleasesPageViewModel viewModel)
        {
            AddReleaseDialogViewModel dialogViewModel = viewModel.CreateAddReleaseDialog();
            var dialog = new AddReleaseDialog
            {
                DataContext = dialogViewModel
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary
                && dialogViewModel.Result != null)
            {
                viewModel.Items.Add(dialogViewModel.Result);
            }
        }
    }

    private void Edit_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is StyledElement { DataContext: Release item })
        {
            NavigateToReleasePage(item);
        }
    }

    private void NavigateToReleasePage(Release release)
    {
        if (DataContext is PackageReleasesPageViewModel viewModel
            && this.FindAncestorOfType<Frame>() is { } frame)
        {
            ReleasePageViewModel? param = frame.FindParameter<ReleasePageViewModel>(x => x.Release.Id == release.Id);
            param ??= viewModel.CreateReleasePage(release);

            frame.Navigate(typeof(ReleasePage), param, SharedNavigationTransitionInfo.Instance);
        }
    }

    private async void Delete_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PackageReleasesPageViewModel viewModel
            && sender is StyledElement { DataContext: Release release }
            && this.FindAncestorOfType<Frame>() is { } frame)
        {
            var dialog = new ContentDialog
            {
                Title = S.DevelopPage.DeleteResource.Title,
                Content = S.DevelopPage.DeleteResource.Content,
                PrimaryButtonText = S.Common.Yes,
                CloseButtonText = S.Common.No,
                DefaultButton = ContentDialogButton.Primary
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                frame.RemoveAllStack(item => item is ReleasePageViewModel p && p.Release.Id == release.Id);

                await viewModel.DeleteReleaseAsync(release);
                frame.GoBack();
            }
        }
    }

    private void NavigatePackageDetailsPage_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PackageReleasesPageViewModel viewModel
            && this.FindAncestorOfType<Frame>() is { } frame)
        {
            PackageDetailsPageViewModel? param = frame.FindParameter<PackageDetailsPageViewModel>(x => x.Package.Id == viewModel.Package.Id);
            param ??= viewModel.CreatePackageDetailsPage();

            frame.Navigate(typeof(PackageDetailsPage), param, SharedNavigationTransitionInfo.Instance);
        }
    }
}
