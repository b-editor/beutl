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
        //ScreenshotsScrollViewer.AddHandler(PointerWheelChangedEvent, ScreenshotsScrollViewer_PointerWheelChanged, RoutingStrategies.Tunnel);
    }

    //private void ScreenshotsScrollViewer_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    //{
    //    Avalonia.Vector offset = ScreenshotsScrollViewer.Offset;

    //    // オフセット(X) をスクロール
    //    ScreenshotsScrollViewer.Offset = offset.WithX(offset.X - (e.Delta.Y * 50));

    //    e.Handled = true;
    //}

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
                frame.RemoveAllStack(item => item is ResourcePageViewModel p
                    && p.Resource.Response.Value.Locale == viewModel.Resource.Response.Value.Locale
                    && p.Resource.Package.Id == viewModel.Resource.Package.Id);

                await viewModel.DeleteAsync();
                frame.GoBack();
            }
        }
    }

    private void NavigatePackageDetailsPage_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ResourcePageViewModel viewModel
            && this.FindAncestorOfType<Frame>() is { } frame)
        {
            PackageDetailsPageViewModel? param = frame.FindParameter<PackageDetailsPageViewModel>(x => x.Package.Id == viewModel.Resource.Package.Id);
            param ??= viewModel.CreatePackageDetailsPage();

            frame.Navigate(typeof(PackageDetailsPage), param, SharedNavigationTransitionInfo.Instance);
        }
    }

    private void NavigatePackageSettingsPage_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ResourcePageViewModel viewModel
            && this.FindAncestorOfType<Frame>() is { } frame)
        {
            PackageSettingsPageViewModel? param = frame.FindParameter<PackageSettingsPageViewModel>(x => x.Package.Id == viewModel.Resource.Package.Id);
            param ??= viewModel.CreatePackageSettingsPage();

            frame.Navigate(typeof(PackageSettingsPage), param, SharedNavigationTransitionInfo.Instance);
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
                //await viewModel.SetLogo.ExecuteAsync(result[0]);
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

            //foreach (IStorageFile item in result)
            //{
            //    await viewModel.AddScreenshot.ExecuteAsync(item);
            //}
        }
    }
}
