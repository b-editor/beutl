using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;

using BeUtl.Pages.ExtensionsPages.DevelopPages.Dialogs;
using BeUtl.ViewModels.ExtensionsPages.DevelopPages;
using BeUtl.ViewModels.ExtensionsPages.DevelopPages.Dialogs;

namespace BeUtl.Pages.ExtensionsPages.DevelopPages;

public sealed partial class PackageSettingsPage : UserControl
{
    public PackageSettingsPage()
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

    private void NavigatePackageDetailsPage_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PackageSettingsPageViewModel viewModel
            && this.FindAncestorOfType<FA.Frame>() is { } frame)
        {
            frame.Navigate(typeof(PackageDetailsPage), viewModel.Parent, SharedNavigationTransitionInfo.Instance);
        }
    }

    private async void AddResource_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PackageSettingsPageViewModel viewModel)
        {
            var dialog = new AddResourceDialog
            {
                DataContext = new AddResourceDialogViewModel(viewModel.Parent.Package.Value)
            };
            await dialog.ShowAsync();
        }
    }

    private void EditResource_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ResourcePageViewModel itemViewModel }
            && this.FindAncestorOfType<FA.Frame>() is { } frame)
        {
            frame.Navigate(typeof(ResourcePage), itemViewModel, SharedNavigationTransitionInfo.Instance);
        }
    }

    private async void DeleteResource_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ResourcePageViewModel itemViewModel }
            && this.FindAncestorOfType<FA.Frame>() is { } frame)
        {
            var dialog = new FA.ContentDialog
            {
                Title = S.DevelopPage.DeleteResource.Title,
                Content = S.DevelopPage.DeleteResource.Content,
                PrimaryButtonText = S.Common.Yes,
                CloseButtonText = S.Common.No,
                DefaultButton = FA.ContentDialogButton.Primary
            };

            if (await dialog.ShowAsync() == FA.ContentDialogResult.Primary)
            {
                string resourceId = itemViewModel.Resource.Value.Snapshot.Id;
                string packageId = itemViewModel.Parent.Reference.Id;
                frame.RemoveAllStack(item => item is ResourcePageViewModel p
                    && p.Resource.Value.Snapshot.Id == resourceId
                    && p.Parent.Reference.Id == packageId);

                itemViewModel.Delete.Execute();
            }
        }
    }

    private async void DeletePackage_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PackageSettingsPageViewModel viewModel
            && this.FindAncestorOfType<FA.Frame>() is { } frame)
        {
            var dialog = new FA.ContentDialog
            {
                Title = S.DevelopPage.DeletePackage.Title,
                Content = S.DevelopPage.DeletePackage.Content,
                PrimaryButtonText = S.Common.Yes,
                CloseButtonText = S.Common.No,
                DefaultButton = FA.ContentDialogButton.Primary
            };

            if (await dialog.ShowAsync() == FA.ContentDialogResult.Primary)
            {
                string packageId = viewModel.Reference.Id;
                frame.RemoveAllStack(
                    item => (item is PackageDetailsPageViewModel p1 && p1.Package.Value.Snapshot.Id == packageId)
                         || (item is PackageSettingsPageViewModel p2 && p2.Reference.Id == packageId)
                         || (item is ResourcePageViewModel p3 && p3.Parent.Reference.Id == packageId)
                         || (item is ReleasePageViewModel p4 && p4.Parent.Parent.Package.Value.Snapshot.Id == packageId)
                         || (item is PackageReleasesPageViewModel p5 && p5.Parent.Package.Value.Snapshot.Id == packageId));

                viewModel.Delete.Execute();

                frame.GoBack();
            }
        }
    }

    private async void MakePublic_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PackageSettingsPageViewModel viewModel)
        {
            var dialog = new FA.ContentDialog
            {
                Title = S.DevelopPage.MakePublicPackage.Title,
                Content = S.DevelopPage.MakePublicPackage.Content,
                PrimaryButtonText = S.Common.Yes,
                CloseButtonText = S.Common.No,
                DefaultButton = FA.ContentDialogButton.Primary
            };

            if (await dialog.ShowAsync() == FA.ContentDialogResult.Primary)
            {
                viewModel.MakePublic.Execute();
            }
        }
    }

    private async void MakePrivate_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PackageSettingsPageViewModel viewModel)
        {
            var dialog = new FA.ContentDialog
            {
                Title = S.DevelopPage.MakePrivatePackage.Title,
                Content = S.DevelopPage.MakePrivatePackage.Content,
                PrimaryButtonText = S.Common.Yes,
                CloseButtonText = S.Common.No,
                DefaultButton = FA.ContentDialogButton.Primary
            };

            if (await dialog.ShowAsync() == FA.ContentDialogResult.Primary)
            {
                viewModel.MakePrivate.Execute();
            }
        }
    }

    private async void OpenLogoFile_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PackageSettingsPageViewModel viewModel
            && this.FindLogicalAncestorOfType<Window>() is { } window)
        {
            var options = new FilePickerOpenOptions
            {
                FileTypeFilter = new FilePickerFileType[]
                {
                    FilePickerFileTypes.ImageAll
                }
            };
            IReadOnlyList<IStorageFile> result = await window.StorageProvider.OpenFilePickerAsync(options);
            if (result.Count > 0)
            {
                await viewModel.SetLogo.ExecuteAsync(result[0]);
            }
        }
    }

    private async void AddScreenshotFile_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PackageSettingsPageViewModel viewModel
            && this.FindLogicalAncestorOfType<Window>() is { } window)
        {
            var options = new FilePickerOpenOptions
            {
                AllowMultiple = true,
                FileTypeFilter = new FilePickerFileType[]
                {
                    FilePickerFileTypes.ImageAll
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
