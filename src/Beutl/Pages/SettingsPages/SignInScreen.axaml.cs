using Avalonia.Controls;
using Avalonia.Styling;

using FluentAvalonia.Styling;

namespace Beutl.Pages.SettingsPages;
public partial class SignInScreen : UserControl
{
    public SignInScreen()
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
}
