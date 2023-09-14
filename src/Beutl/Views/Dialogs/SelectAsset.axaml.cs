using Avalonia.Interactivity;

using Beutl.Api.Objects;

using Beutl.ViewModels.Dialogs;

using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Dialogs;

public partial class SelectAsset : ContentDialog
{
    public SelectAsset()
    {
        InitializeComponent();
    }

    protected override Type StyleKeyOverride => typeof(ContentDialog);

    private async void Add_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SelectAssetViewModel viewModel)
        {
            CreateAssetViewModel dialogViewModel = viewModel.CreateAssetViewModel();
            var dialog = new CreateAsset
            {
                DataContext = dialogViewModel,
            };

            await dialog.ShowAsync();

            if (dialogViewModel.Result is Asset asset)
            {
                await viewModel.Refresh.ExecuteAsync();
                SelectAssetViewModel.AssetViewModel? itemViewModel
                    = viewModel.Items.FirstOrDefault(x => x.Model.Id == asset.Id);

                if (itemViewModel != null)
                {
                    viewModel.SelectedItem.Value = itemViewModel;
                }
            }
        }
    }
}
