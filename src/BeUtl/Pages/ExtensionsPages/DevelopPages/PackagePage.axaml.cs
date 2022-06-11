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
            var dialog = new ContentDialog
            {
                Title = "パッケージを削除",
                Content = "パッケージを削除してもよろしいですか？",
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
}
