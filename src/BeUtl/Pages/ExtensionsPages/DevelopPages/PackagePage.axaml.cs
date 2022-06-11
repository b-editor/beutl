using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using BeUtl.Controls;
using BeUtl.ViewModels.ExtensionsPages.DevelopPages;

using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Media.Animation;

namespace BeUtl.Pages.ExtensionsPages.DevelopPages;

public partial class PackagePage : UserControl
{
    public PackagePage()
    {
        InitializeComponent();
    }

    private void NavigateToResourceSettings_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PackagePageViewModel viewModel)
        {
            Frame frame = this.FindAncestorOfType<Frame>();
            var transitionInfo = new EntranceNavigationTransitionInfo
            {
                FromHorizontalOffset = 28,
                FromVerticalOffset = 0
            };

            frame.Navigate(typeof(MoreResourcesPage), viewModel.ResourcesViewModel, transitionInfo);
        }
    }

    private async void DeletePackage_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PackagePageViewModel viewModel)
        {
            Frame frame = this.FindAncestorOfType<Frame>();
            var dialog = new ContentDialog
            {
                Title = "パッケージを削除",
                Content = "パッケージを削除してもよろしいですか？\nこの操作を実行するとこのパッケージには二度とアクセスできなくなります。",
                PrimaryButtonText = "はい",
                CloseButtonText = "いいえ",
                DefaultButton = ContentDialogButton.Primary
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                viewModel.Delete.Execute();
                frame.GoBack();
            }
        }
    }

    private async void MakePublic_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PackagePageViewModel viewModel)
        {
            var dialog = new ContentDialog
            {
                Title = "パッケージを公開",
                Content = "パッケージを公開してもよろしいですか？\nこの操作を実行すると他人がこのパッケージをダウンロードできるようになります。",
                PrimaryButtonText = "はい",
                CloseButtonText = "いいえ",
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
        if (DataContext is PackagePageViewModel viewModel)
        {
            var dialog = new ContentDialog
            {
                Title = "パッケージを非公開にする",
                Content = "パッケージを非公開にしてもよろしいですか？\nこの操作を実行すると他人がこのパッケージをダウンロードできなくなります。",
                PrimaryButtonText = "はい",
                CloseButtonText = "いいえ",
                DefaultButton = ContentDialogButton.Primary
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                viewModel.MakePrivate.Execute();
            }
        }
    }
}
