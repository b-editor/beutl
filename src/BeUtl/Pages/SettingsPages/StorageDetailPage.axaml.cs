using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

using BeUtl.ViewModels.SettingsPages;
using BeUtl.Views.Dialogs;

using FluentAvalonia.UI.Controls;

using static BeUtl.ViewModels.SettingsPages.StorageDetailPageViewModel;

namespace BeUtl.Pages.SettingsPages;

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
                Title = current ? S.StorageDetailPage.MakePrivate : S.StorageDetailPage.MakePublic,
                Content = string.Format(S.StorageDetailPage.ChangeVisibility, current ? S.Common.Private : S.Common.Public),
                PrimaryButtonText = S.Common.OK,
                CloseButtonText = S.Common.Cancel
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await itemViewModel.Model.UpdateAsync(
                    new Beutl.Api.UpdateAssetRequest(!current));
            }
        }
    }

    public async void CopyDownloadUrl_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is CommandBarButton { DataContext: AssetViewModel itemViewModel }
            && Application.Current?.Clipboard is { } clipboard)
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
                Title = S.Message.DoYouWantToDeleteThisFile,
                Content = $"{S.Message.DoYouWantToDeleteThisFile}\n" +
                $"{itemViewModel.Model.Name} | {itemViewModel.ShortUrl} | {itemViewModel.Model.ContentType} | {itemViewModel.UsedCapacity}",
                PrimaryButtonText = S.Common.Delete,
                CloseButtonText = S.Common.Cancel
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await viewModel.DeleteAsync(itemViewModel);
            }
        }
    }

    private async void UploadClick(object? sender, RoutedEventArgs e)
    {
        // Todo:
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
