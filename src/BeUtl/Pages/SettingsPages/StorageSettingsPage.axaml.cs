using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using BeUtl.Controls;
using BeUtl.ViewModels.SettingsPages;

using FluentAvalonia.UI.Controls;

namespace BeUtl.Pages.SettingsPages;

public sealed partial class StorageSettingsPage : UserControl
{
    public StorageSettingsPage()
    {
        InitializeComponent();
    }

    private void NavigateToStorageDetail(object? sender, RoutedEventArgs e)
    {
        if (DataContext is StorageSettingsPageViewModel viewModel
            && sender is OptionsDisplayItem { DataContext: StorageSettingsPageViewModel.DetailItem itemViewModel }
            && this.FindAncestorOfType<Frame>() is { } frame)
        {
            StorageDetailPageViewModel? param = frame.FindParameter<StorageDetailPageViewModel>(x => x.Type == itemViewModel.Type);
            param ??= viewModel.CreateDetailPage(itemViewModel.Type);

            if (param != null)
                frame.Navigate(typeof(StorageDetailPage), param, SharedNavigationTransitionInfo.Instance);
        }
    }
}
