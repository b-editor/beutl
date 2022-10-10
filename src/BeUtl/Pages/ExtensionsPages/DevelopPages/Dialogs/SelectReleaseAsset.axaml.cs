using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;

using Beutl.Api.Objects;

using BeUtl.Pages.SettingsPages.Dialogs;
using BeUtl.ViewModels.ExtensionsPages.DevelopPages.Dialogs;
using BeUtl.ViewModels.SettingsPages.Dialogs;

using FluentAvalonia.UI.Controls;

namespace BeUtl.Pages.ExtensionsPages.DevelopPages.Dialogs;

public partial class SelectReleaseAsset : ContentDialog, IStyleable
{
    public SelectReleaseAsset()
    {
        InitializeComponent();
    }

    Type IStyleable.StyleKey => typeof(ContentDialog);

    private async void Add_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SelectReleaseAssetViewModel viewModel)
        {
            CreateAssetViewModel dialogViewModel = viewModel.CreateAssetViewModel();
            var dialog = new CreateAsset
            {
                DataContext = dialogViewModel,
            };

            Hide();
            await dialog.ShowAsync();

            if (dialogViewModel.Result is Asset asset)
            {
                await viewModel.Refresh.ExecuteAsync();
                SelectReleaseAssetViewModel.AssetViewModel? itemViewModel
                    = viewModel.Items.FirstOrDefault(x => x.Model.Id == asset.Id);

                if (itemViewModel != null)
                {
                    viewModel.SelectedItem.Value = itemViewModel;
                }
            }

            await ShowAsync();
        }
    }
}
