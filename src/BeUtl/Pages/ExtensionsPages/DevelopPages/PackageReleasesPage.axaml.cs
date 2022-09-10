using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

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
            if (ReleasesList.SelectedItem is ReleasePageViewModel item
                && this.FindAncestorOfType<Frame>() is { } frame)
            {
                frame.Navigate(typeof(ReleasePage), item, SharedNavigationTransitionInfo.Instance);
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
            var dialogViewModel = new AddReleaseDialogViewModel();
            var dialog = new AddReleaseDialog
            {
                DataContext = dialogViewModel
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                viewModel.Add.Execute(dialogViewModel);
            }
        }
    }

    private void Edit_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is StyledElement { DataContext: ReleasePageViewModel item }
            && this.FindAncestorOfType<Frame>() is { } frame)
        {
            frame.Navigate(typeof(ReleasePage), item, SharedNavigationTransitionInfo.Instance);
        }
    }

    private async void Delete_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is StyledElement { DataContext: ReleasePageViewModel item }
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
                string releaseId = item.Release.Value.Snapshot.Id;
                string packageId = item.Parent.Parent.Package.Value.Snapshot.Id;
                frame.RemoveAllStack(item => item is ReleasePageViewModel p
                    && p.Release.Value.Snapshot.Id == releaseId
                    && p.Parent.Parent.Package.Value.Snapshot.Id == packageId);

                item.Delete.Execute();
            }
        }
    }

    private void NavigatePackageDetailsPage_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PackageReleasesPageViewModel viewModel
            && this.FindAncestorOfType<Frame>() is { } frame)
        {
            frame.Navigate(typeof(PackageDetailsPage), viewModel.Parent, SharedNavigationTransitionInfo.Instance);
        }
    }
}
