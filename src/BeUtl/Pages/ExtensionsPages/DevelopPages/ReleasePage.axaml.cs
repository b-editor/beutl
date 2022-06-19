using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using BeUtl.Pages.ExtensionsPages.DevelopPages.Dialogs;
using BeUtl.ViewModels.ExtensionsPages.DevelopPages;
using BeUtl.ViewModels.ExtensionsPages.DevelopPages.Dialogs;

using FluentAvalonia.UI.Controls;

using Button = Avalonia.Controls.Button;

namespace BeUtl.Pages.ExtensionsPages.DevelopPages;

public partial class ReleasePage : UserControl
{
    public ReleasePage()
    {
        InitializeComponent();
    }

    private void NavigatePackageDetailsPage_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ReleasePageViewModel viewModel)
        {
            Frame frame = this.FindAncestorOfType<Frame>();
            frame.Navigate(typeof(PackageDetailsPage), viewModel.Parent.Parent, SharedNavigationTransitionInfo.Instance);
        }
    }

    private void NavigatePackageReleasesPage_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ReleasePageViewModel viewModel)
        {
            Frame frame = this.FindAncestorOfType<Frame>();
            frame.Navigate(typeof(PackageReleasesPage), viewModel.Parent, SharedNavigationTransitionInfo.Instance);
        }
    }

    private async void DeleteRelease_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ReleasePageViewModel viewModel)
        {
            Frame frame = this.FindAncestorOfType<Frame>();
            var dialog = new ContentDialog
            {
                Title = "リリースを削除",
                Content = "リリースを削除してもよろしいですか？\nこの操作を実行するとこのリリースには二度とアクセスできなくなります。",
                PrimaryButtonText = "はい",
                CloseButtonText = "いいえ",
                DefaultButton = ContentDialogButton.Primary
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                string releaseId = viewModel.Release.Value.Snapshot.Id;
                string packageId = viewModel.Parent.Parent.Reference.Id;
                frame.RemoveAllStack(item => item is ReleasePageViewModel p
                    && p.Release.Value.Snapshot.Id == releaseId
                    && p.Parent.Parent.Reference.Id == packageId);

                viewModel.Delete.Execute();
                frame.GoBack();
            }
        }
    }

    private async void AddResource_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ReleasePageViewModel viewModel)
        {
            var dialog = new AddReleaseResourceDialog
            {
                DataContext = new AddReleaseResourceDialogViewModel(viewModel.Release.Value)
            };
            await dialog.ShowAsync();
        }
    }

    private async void EditResource_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ReleaseResourceViewModel itemViewModel })
        {
            var dialog = new EditReleaseResourceDialog
            {
                DataContext = new EditReleaseResourceDialogViewModel(itemViewModel.Resource)
            };
            await dialog.ShowAsync();
        }
    }

    private async void DeleteResource_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ReleaseResourceViewModel itemViewModel })
        {
            var dialog = new ContentDialog
            {
                Title = "リソースを削除",
                Content = "リソースを削除してもよろしいですか？",
                PrimaryButtonText = "はい",
                CloseButtonText = "いいえ",
                DefaultButton = ContentDialogButton.Primary
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                itemViewModel.Delete.Execute();
            }
        }
    }

    private async void MakePublic_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ReleasePageViewModel viewModel)
        {
            var dialog = new ContentDialog
            {
                Title = "リリースを公開",
                Content = "リリースを公開してもよろしいですか？\nこの操作を実行すると他人がこのリリースをダウンロードできるようになります。",
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
        if (DataContext is ReleasePageViewModel viewModel)
        {
            var dialog = new ContentDialog
            {
                Title = "リリースを非公開にする",
                Content = "リリースを非公開にしてもよろしいですか？\nこの操作を実行すると他人がこのリリースをダウンロードできなくなります。",
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
