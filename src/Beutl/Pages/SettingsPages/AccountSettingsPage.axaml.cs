using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Styling;

using Beutl.ViewModels.Dialogs;
using Beutl.ViewModels.SettingsPages;
using Beutl.Views.Dialogs;

using FluentAvalonia.Styling;

namespace Beutl.Pages.SettingsPages;

public sealed partial class AccountSettingsPage : UserControl
{
    public AccountSettingsPage()
    {
        InitializeComponent();
        OnActualThemeVariantChanged(null, EventArgs.Empty);
        ActualThemeVariantChanged += OnActualThemeVariantChanged;
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        ThemeVariant theme = ActualThemeVariant;
        if (theme == ThemeVariant.Light || theme == FluentAvaloniaTheme.HighContrastTheme)
        {
            githubLightLogo.IsVisible = true;
            githubDarkLogo.IsVisible = false;
        }
        else
        {
            githubLightLogo.IsVisible = false;
            githubDarkLogo.IsVisible = true;
        }
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
