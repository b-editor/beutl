using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

using BeUtl.ViewModels.Dialogs;
using BeUtl.ViewModels.SettingsPages;
using BeUtl.Views.Dialogs;

using FluentAvalonia.Styling;

namespace BeUtl.Pages.SettingsPages;

public sealed partial class AccountSettingsPage : UserControl
{
    private FluentAvaloniaTheme _theme;

    public AccountSettingsPage()
    {
        InitializeComponent();
        _theme = AvaloniaLocator.Current.GetRequiredService<FluentAvaloniaTheme>();
    }

    protected override void OnAttachedToLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        _theme.RequestedThemeChanged += Theme_RequestedThemeChanged;
        OnThemeChanged(_theme.RequestedTheme);
    }

    protected override void OnDetachedFromLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromLogicalTree(e);
        _theme.RequestedThemeChanged -= Theme_RequestedThemeChanged;
    }

    private void Theme_RequestedThemeChanged(FluentAvaloniaTheme sender, RequestedThemeChangedEventArgs args)
    {
        OnThemeChanged(args.NewTheme);
    }

    private void OnThemeChanged(string theme)
    {
        switch (theme)
        {
            case "Light" or "HightContrast":
                githubLightLogo.IsVisible = true;
                githubDarkLogo.IsVisible = false;
                break;
            case "Dark":
                githubLightLogo.IsVisible = false;
                githubDarkLogo.IsVisible = true;
                break;
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
