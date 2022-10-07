using Avalonia.Controls;
using Avalonia.Interactivity;

using BeUtl.Pages.SettingsPages.Dialogs;
using BeUtl.ViewModels.SettingsPages;

using FluentAvalonia.UI.Controls;

using static BeUtl.ViewModels.SettingsPages.StorageDetailPageViewModel;

namespace BeUtl.Pages.SettingsPages;

public partial class StorageDetailPage : UserControl
{
    public StorageDetailPage()
    {
        InitializeComponent();
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
