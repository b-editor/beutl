using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

using BeUtl.ViewModels.ExtensionsPages.DevelopPages;

using FluentAvalonia.UI.Controls;

using S = BeUtl.Language.StringResources;

namespace BeUtl.Pages.ExtensionsPages.DevelopPages;

public sealed partial class ResourcePage : UserControl
{
    public ResourcePage()
    {
        InitializeComponent();
        ScreenshotsScrollViewer.AddHandler(PointerWheelChangedEvent, ScreenshotsScrollViewer_PointerWheelChanged, RoutingStrategies.Tunnel);
    }

    private void ScreenshotsScrollViewer_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        Avalonia.Vector offset = ScreenshotsScrollViewer.Offset;

        // オフセット(X) をスクロール
        ScreenshotsScrollViewer.Offset = offset.WithX(offset.X - (e.Delta.Y * 50));

        e.Handled = true;
    }

    private async void DeleteResource_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ResourcePageViewModel viewModel)
        {
            Frame frame = this.FindAncestorOfType<Frame>();
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
                string resourceId = viewModel.Resource.Value.Snapshot.Id;
                string packageId = viewModel.Parent.Reference.Id;
                frame.RemoveAllStack(item => item is ResourcePageViewModel p
                    && p.Resource.Value.Snapshot.Id == resourceId
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

    private async void AddScreenshotFile_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ResourcePageViewModel viewModel)
        {
            Window? window = this.FindLogicalAncestorOfType<Window>();
            var dialog = new OpenFileDialog
            {
                AllowMultiple = true,
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
                foreach (string item in items)
                {
                    viewModel.AddScreenshot.Execute(item);
                }
            }
        }
    }
}
