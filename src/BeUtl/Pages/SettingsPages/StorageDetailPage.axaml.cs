using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using BeUtl.Pages.SettingsPages.Dialogs;
using BeUtl.ViewModels.SettingsPages;

using FluentAvalonia.UI.Controls;

namespace BeUtl.Pages.SettingsPages;

public partial class StorageDetailPage : UserControl
{
    public StorageDetailPage()
    {
        InitializeComponent();
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

    private void NavigateStorageSettingsPage_Click(object? sender, RoutedEventArgs e)
    {
        if (this.FindAncestorOfType<Frame>() is { } frame)
        {
            frame.Navigate(typeof(StorageSettingsPage), null, SharedNavigationTransitionInfo.Instance);
        }
    }
}
