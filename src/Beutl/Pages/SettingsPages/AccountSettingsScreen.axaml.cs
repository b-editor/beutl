using Avalonia.Controls;
using Avalonia.Interactivity;

using Beutl.ViewModels.Dialogs;
using Beutl.ViewModels.SettingsPages;
using Beutl.Views.Dialogs;

namespace Beutl.Pages.SettingsPages;
public partial class AccountSettingsScreen : UserControl
{
    public AccountSettingsScreen()
    {
        InitializeComponent();
    }

    private async void UpdateProfileImage_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AccountSettingsPageViewModel viewModel)
        {
            SelectImageAssetViewModel dialogViewModel = viewModel.CreateSelectAvatarImage();
            var dialog = new SelectImageAsset
            {
                DataContext = dialogViewModel
            };

            await dialog.ShowAsync();

            if (dialogViewModel.SelectedItem.Value is { } selectedItem)
            {
                await viewModel.UpdateAvatarImage(selectedItem);
            }
        }
    }
}
