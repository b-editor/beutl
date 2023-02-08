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
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        OnThemeChanged(Application.Current!.ActualThemeVariant);
    }

    protected override void OnAttachedToLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        Application.Current!.ActualThemeVariantChanged += OnActualThemeVariantChanged;
        OnThemeChanged(Application.Current!.ActualThemeVariant);
    }

    protected override void OnDetachedFromLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromLogicalTree(e);
        Application.Current!.ActualThemeVariantChanged -= OnActualThemeVariantChanged;
    }

    private void OnThemeChanged(ThemeVariant theme)
    {
        if (theme == ThemeVariant.Light
            || theme == FluentAvaloniaTheme.HighContrastTheme)
        {
            githubLightLogo.IsVisible = true;
            githubDarkLogo.IsVisible = false;
        }
        else if (theme == ThemeVariant.Dark)
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
