using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
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
                string resourceId = viewModel.Reference.Id;
                string packageId = viewModel.Parent.Reference.Id;
                frame.RemoveAllStack(item => item is ResourcePageViewModel p
                    && p.Reference.Id == resourceId
                    && p.Parent.Reference.Id == packageId);

                viewModel.Delete.Execute();
                frame.GoBack();
            }
        }
    }

    private void NavigatePackageDetailsPage_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ResourcePageViewModel viewModel)
        {
            Frame frame = this.FindAncestorOfType<Frame>();
            frame.Navigate(typeof(PackageDetailsPage), viewModel.Parent.Parent, SharedNavigationTransitionInfo.Instance);
        }
    }

    private void NavigatePackageSettingsPage_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ResourcePageViewModel viewModel)
        {
            Frame frame = this.FindAncestorOfType<Frame>();
            frame.Navigate(typeof(PackageSettingsPage), viewModel.Parent, SharedNavigationTransitionInfo.Instance);
        }
    }

    private async void OpenLogoFile_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ResourcePageViewModel viewModel)
        {
            Window? window = this.FindLogicalAncestorOfType<Window>();
            var dialog = new OpenFileDialog
            {
                AllowMultiple = false,
                Filters = new()
                {
                    new FileDialogFilter()
                    {
                        Extensions = { "jpg", "jpeg", "png" }
                    }
                }
            };
            if ((await dialog.ShowAsync(window)) is string[] items && items.Length > 0)
            {
                viewModel.SetLogo.Execute(items[0]);
            }
        }
    }
}
