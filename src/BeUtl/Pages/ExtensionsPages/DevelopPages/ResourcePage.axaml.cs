using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using BeUtl.ViewModels.ExtensionsPages.DevelopPages;

using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Media.Animation;

namespace BeUtl.Pages.ExtensionsPages.DevelopPages;

public partial class ResourcePage : UserControl
{
    public ResourcePage()
    {
        InitializeComponent();
    }

    private async void DeleteResource_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ResourcePageViewModel viewModel)
        {
            Frame frame = this.FindAncestorOfType<Frame>();
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
                viewModel.Delete.Execute();
                frame.GoBack();
            }
        }
    }

    private void NavigatePackagePage_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ResourcePageViewModel viewModel)
        {
            Frame frame = this.FindAncestorOfType<Frame>();
            var transitionInfo = new EntranceNavigationTransitionInfo
            {
                FromHorizontalOffset = -28,
                FromVerticalOffset = 0
            };

            frame.Navigate(typeof(PackagePage), viewModel.Parent.Parent, transitionInfo);
        }
    }

    private void NavigateMoreResourcesPage_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ResourcePageViewModel viewModel)
        {
            Frame frame = this.FindAncestorOfType<Frame>();
            var transitionInfo = new EntranceNavigationTransitionInfo
            {
                FromHorizontalOffset = -28,
                FromVerticalOffset = 0
            };

            frame.Navigate(typeof(MoreResourcesPage), viewModel.Parent, transitionInfo);
        }
    }
}
