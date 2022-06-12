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

public partial class PackageReleasesPage : UserControl
{
    private bool flag;

    public PackageReleasesPage()
    {
        InitializeComponent();
        ReleasesList.AddHandler(PointerPressedEvent, ReleasesList_PointerPressed, RoutingStrategies.Tunnel);
        ReleasesList.AddHandler(PointerReleasedEvent, ReleasesList_PointerReleased, RoutingStrategies.Tunnel);
    }

    private void ReleasesList_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (flag)
        {
            if (ReleasesList.SelectedItem is ReleasePageViewModel item)
            {
                Frame frame = this.FindAncestorOfType<Frame>();
                frame.Navigate(typeof(ReleasePage), item, SharedNavigationTransitionInfo.Instance);
            }
            flag = false;
        }
    }

    private void ReleasesList_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            flag = true;
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
        if (sender is StyledElement { DataContext: ReleasePageViewModel item })
        {
            Frame frame = this.FindAncestorOfType<Frame>();
            frame.Navigate(typeof(ReleasePage), item, SharedNavigationTransitionInfo.Instance);
        }
    }

    private async void Delete_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is StyledElement { DataContext: ReleasePageViewModel item })
        {
            var dialog = new ContentDialog
            {
                Title = "リリースを削除",
                Content = "リリースを削除してもよろしいですか？",
                PrimaryButtonText = "はい",
                CloseButtonText = "いいえ",
                DefaultButton = ContentDialogButton.Primary
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                item.Delete.Execute();
            }
        }
    }

    private void NavigatePackageDetailsPage_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PackageReleasesPageViewModel viewModel)
        {
            Frame frame = this.FindAncestorOfType<Frame>();
            frame.Navigate(typeof(PackageDetailsPage), viewModel.Parent, SharedNavigationTransitionInfo.Instance);
        }
    }
}
