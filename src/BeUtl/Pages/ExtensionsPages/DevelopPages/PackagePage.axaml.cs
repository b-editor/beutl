using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using BeUtl.ViewModels.ExtensionsPages.DevelopPages;

using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Media.Animation;
using FluentAvalonia.UI.Navigation;

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
            int type = Random.Shared.Next(0, 2);
            var dialog = new ContentDialog
            {
                Title = "パッケージを削除",
                Content = "パッケージを削除してもよろしいですか？",
                PrimaryButtonText = type == 0 ? "はい" : "いいえ",
                SecondaryButtonText = type == 0 ? "いいえ" : "はい",
                DefaultButton = (ContentDialogButton)Random.Shared.Next(0, 4)
            };

            if (type == 0) dialog.IsPrimaryButtonEnabled = false;
            else dialog.IsSecondaryButtonEnabled = false;

            Task<ContentDialogResult> task = dialog.ShowAsync();

            await Task.Delay(Random.Shared.Next(0, 1000));
            if (type == 0) dialog.IsPrimaryButtonEnabled = true;
            else dialog.IsSecondaryButtonEnabled = true;

            ContentDialogResult result = await task;

            if ((result == ContentDialogResult.Primary && type == 0)
                || (result == ContentDialogResult.Secondary && type == 1))
            {
                viewModel.Delete.Execute();
                frame.GoBack();
            }
        }
    }
}
