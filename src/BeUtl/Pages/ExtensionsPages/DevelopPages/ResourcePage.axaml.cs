using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;

using BeUtl.ViewModels.ExtensionsPages.DevelopPages;

using FluentAvalonia.UI.Controls;

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
        if (DataContext is ResourcePageViewModel viewModel
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
            Frame? frame = this.FindAncestorOfType<Frame>();
            frame?.Navigate(typeof(PackageDetailsPage), viewModel.Parent.Parent, SharedNavigationTransitionInfo.Instance);
        }
    }

    private void NavigatePackageSettingsPage_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ResourcePageViewModel viewModel)
        {
            Frame? frame = this.FindAncestorOfType<Frame>();
            frame?.Navigate(typeof(PackageSettingsPage), viewModel.Parent, SharedNavigationTransitionInfo.Instance);
        }
    }

    private async void OpenLogoFile_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ResourcePageViewModel viewModel
            && this.FindLogicalAncestorOfType<Window>() is { } window)
        {
            var options = new FilePickerOpenOptions
            {
                FileTypeFilter = new FilePickerFileType[]
                {
                    FilePickerFileTypes.ImagePng
                }
            };

            IReadOnlyList<IStorageFile> result = await window.StorageProvider.OpenFilePickerAsync(options);
            if (result?.Count > 0)
            {
                await viewModel.SetLogo.ExecuteAsync(result[0]);
            }
        }
    }

    private async void AddScreenshotFile_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ResourcePageViewModel viewModel
            && this.FindLogicalAncestorOfType<Window>() is { } window)
        {
            var options = new FilePickerOpenOptions
            {
                AllowMultiple = true,
                FileTypeFilter = new FilePickerFileType[]
                {
                    FilePickerFileTypes.ImagePng
                }
            };

            IReadOnlyList<IStorageFile> result = await window.StorageProvider.OpenFilePickerAsync(options);

            foreach (IStorageFile item in result)
            {
                await viewModel.AddScreenshot.ExecuteAsync(item);
            }
        }
    }
}
