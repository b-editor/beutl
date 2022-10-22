using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;

using Beutl.Api.Objects;

using BeUtl.ViewModels;
using BeUtl.ViewModels.ExtensionsPages.DevelopPages;

using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Navigation;

namespace BeUtl.Pages.ExtensionsPages.DevelopPages;

public sealed partial class PackageSettingsPage : UserControl
{
    public PackageSettingsPage()
    {
        InitializeComponent();
        //ScreenshotsScrollViewer.AddHandler(PointerWheelChangedEvent, ScreenshotsScrollViewer_PointerWheelChanged, RoutingStrategies.Tunnel);

        AddHandler(Frame.NavigatedFromEvent, OnNavigatedFrom, RoutingStrategies.Direct);
        AddHandler(Frame.NavigatedToEvent, OnNavigatedTo, RoutingStrategies.Direct);
    }

    private void OnNavigatedTo(object? sender, NavigationEventArgs e)
    {
        if (e.Parameter is Package package)
        {
            DestoryDataContext();
            DataContextFactory factory = GetDataContextFactory();
            DataContext = factory.PackageSettingsPage(package);
        }
    }

    private void OnNavigatedFrom(object? sender, NavigationEventArgs e)
    {
        DestoryDataContext();
    }

    private void DestoryDataContext()
    {
        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }

        DataContext = null;
    }

    private DataContextFactory GetDataContextFactory()
    {
        return ((ExtensionsPageViewModel)this.FindLogicalAncestorOfType<ExtensionsPage>()!.DataContext!).Develop.DataContextFactory;
    }

    //private void ScreenshotsScrollViewer_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    //{
    //    Avalonia.Vector offset = ScreenshotsScrollViewer.Offset;

    //    // オフセット(X) をスクロール
    //    ScreenshotsScrollViewer.Offset = offset.WithX(offset.X - (e.Delta.Y * 50));

    //    e.Handled = true;
    //}

    private void NavigatePackageDetailsPage_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PackageSettingsPageViewModel viewModel
            && this.FindAncestorOfType<Frame>() is { } frame)
        {
            frame.Navigate(typeof(PackageDetailsPage), viewModel.Package, SharedNavigationTransitionInfo.Instance);
        }
    }

    private async void DeletePackage_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PackageSettingsPageViewModel viewModel
            && this.FindAncestorOfType<Frame>() is { } frame)
        {
            var dialog = new ContentDialog
            {
                Title = S.DevelopPage.DeletePackage.Title,
                Content = S.DevelopPage.DeletePackage.Content,
                PrimaryButtonText = S.Common.Yes,
                CloseButtonText = S.Common.No,
                DefaultButton = ContentDialogButton.Primary
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                frame.RemoveAllStack(
                    item => (item is PackageDetailsPageViewModel p1 && p1.Package.Id == viewModel.Package.Id)
                         || (item is PackageSettingsPageViewModel p2 && p2.Package.Id == viewModel.Package.Id)
                         || (item is ReleasePageViewModel p4 && p4.Release.Package.Id == viewModel.Package.Id)
                         || (item is PackageReleasesPageViewModel p5 && p5.Package.Id == viewModel.Package.Id));

                await viewModel.Package.DeleteAsync();

                frame.GoBack();
            }
        }
    }

    private async void MakePublic_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PackageSettingsPageViewModel viewModel)
        {
            var dialog = new ContentDialog
            {
                Title = S.DevelopPage.MakePublicPackage.Title,
                Content = S.DevelopPage.MakePublicPackage.Content,
                PrimaryButtonText = S.Common.Yes,
                CloseButtonText = S.Common.No,
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
        if (DataContext is PackageSettingsPageViewModel viewModel)
        {
            var dialog = new ContentDialog
            {
                Title = S.DevelopPage.MakePrivatePackage.Title,
                Content = S.DevelopPage.MakePrivatePackage.Content,
                PrimaryButtonText = S.Common.Yes,
                CloseButtonText = S.Common.No,
                DefaultButton = ContentDialogButton.Primary
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
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
                // Todo
                //await viewModel.SetLogo.ExecuteAsync(result[0]);
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
            // Todo
            //foreach (IStorageFile item in result)
            //{
            //    await viewModel.AddScreenshot.ExecuteAsync(item);
            //}
        }
    }
}
