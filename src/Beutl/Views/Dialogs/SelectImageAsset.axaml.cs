using Avalonia.Controls;
using Avalonia.Interactivity;

using Beutl.Api.Objects;
using Beutl.ViewModels.Dialogs;

using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Dialogs;

public partial class SelectImageAsset : ContentDialog
{
    public SelectImageAsset()
    {
        InitializeComponent();
    }

    protected override Type StyleKeyOverride => typeof(ContentDialog);

    private async void UploadImage_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SelectImageAssetViewModel viewModel && VisualRoot is Window parent)
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
                Asset? itemViewModel
                    = viewModel.Items.FirstOrDefault(x => x.Id == asset.Id);

                if (itemViewModel != null)
                {
                    viewModel.SelectedItem.Value = itemViewModel;
                }
            }

            await ShowAsync();
        }
    }
}
