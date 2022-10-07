using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

using BeUtl.Pages.SettingsPages.Dialogs;
using BeUtl.ViewModels.SettingsPages;

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
            var dialog = new SelectAvatarImage
            {
                DataContext = viewModel.CreateSelectAvatarImage()
            };

            await dialog.ShowAsync();
        }
    }
}
