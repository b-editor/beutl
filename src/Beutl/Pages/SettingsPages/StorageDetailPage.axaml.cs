using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

using Beutl.ViewModels.SettingsPages;
using Beutl.Views.Dialogs;

using FluentAvalonia.UI.Controls;

using static Beutl.ViewModels.SettingsPages.StorageDetailPageViewModel;

namespace Beutl.Pages.SettingsPages;

public partial class StorageDetailPage : UserControl
{
    public StorageDetailPage()
    {
        InitializeComponent();
    }

    public async void ChangeVisibility_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is CommandBarButton { DataContext: AssetViewModel itemViewModel })
        {
            bool current = itemViewModel.Model.IsPublic.Value;
            var dialog = new ContentDialog
            {
                Title = current ? Language.SettingsPage.MakePrivate : Language.SettingsPage.MakePublic,
                Content = string.Format(Language.SettingsPage.ChangeVisibility, current ? Strings.Private : Strings.Public),
                PrimaryButtonText = Strings.OK,
                CloseButtonText = Strings.Cancel
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                using (await itemViewModel.Model.Lock.LockAsync())
                {
                    await itemViewModel.Model.UpdateAsync(
                        new Api.UpdateAssetRequest(!current));
                }
            }
        }
    }

    public async void CopyDownloadUrl_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is { Clipboard: IClipboard clipboard }
            && sender is CommandBarButton { DataContext: AssetViewModel itemViewModel })
        {
            await clipboard.SetTextAsync(itemViewModel.Model.DownloadUrl);
        }
    }

    private async void Delete_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is StorageDetailPageViewModel viewModel
            && sender is CommandBarButton { DataContext: AssetViewModel itemViewModel })
        {
            var dialog = new ContentDialog
            {
                Title = Message.DoYouWantToDeleteThisFile,
                Content = $"{Message.DoYouWantToDeleteThisFile}\n" +
                $"{itemViewModel.Model.Name} | {itemViewModel.ShortUrl} | {itemViewModel.Model.ContentType} | {itemViewModel.UsedCapacity}",
                PrimaryButtonText = Strings.Delete,
                CloseButtonText = Strings.Cancel
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await viewModel.DeleteAsync(itemViewModel);
            }
        }
    }

    private async void UploadClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is StorageDetailPageViewModel viewModel)
        {
            var dialog = new CreateAsset
            {
                DataContext = viewModel.CreateAssetViewModel()
            };

            await dialog.ShowAsync();
        }
    }
}
