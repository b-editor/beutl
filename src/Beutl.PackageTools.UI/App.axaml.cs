using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;

using Beutl.Configuration;
using Beutl.Controls.Styling;
using Beutl.Controls.Styling.Themes;
using Beutl.PackageTools.UI.Views;

using FluentAvalonia.Styling;

namespace Beutl.PackageTools.UI;

public partial class App : Application
{
    public override void Initialize()
    {
        GlobalConfiguration config = GlobalConfiguration.Instance;
        ViewConfig view = config.ViewConfig;
        CultureInfo.CurrentUICulture = view.UICulture;

        AvaloniaXamlLoader.Load(this);
        var theme = (FluentAvaloniaTheme)Styles[0];

        Color? designAccent = null;
        switch (view.Theme)
        {
            case BuiltinThemeIds.Light:
                RequestedThemeVariant = ThemeVariant.Light;
                break;
            case BuiltinThemeIds.Dark:
                RequestedThemeVariant = ThemeVariant.Dark;
                break;
            case BuiltinThemeIds.HighContrast:
                RequestedThemeVariant = FluentAvaloniaTheme.HighContrastTheme;
                break;
            case BuiltinThemeIds.System:
                theme.PreferSystemTheme = true;
                break;
            case FirstPartyThemeIds.DarkBorder:
                // The default theme. Its ThemeExtension is out of reach here (no extensions load), so
                // apply what ThemeService would: the Dark base variant plus the override dictionary.
                RequestedThemeVariant = ThemeVariant.Dark;
                Resources.MergedDictionaries.Add(
                    (IResourceProvider)AvaloniaXamlLoader.Load(BeutlDarkBorderTheme.ResourceUri, null)!);
                designAccent = BeutlDarkBorderTheme.AccentColor;
                break;
            default:
                // PackageTools.UI loads no extensions/ThemeRegistry, so a custom theme id can't be
                // resolved here — fall back to Dark rather than carry an unknown variant.
                RequestedThemeVariant = ThemeVariant.Dark;
                break;
        }


        // Accent priority mirrors ThemeService, and so does the text-on-accent derivation: this shell
        // shows the same accent surfaces, so a light custom accent would leave the theme's white
        // labels on a near-white fill.
        Color? accent = AccentResolution.Normalize(
            view.UseCustomAccentColor && Color.TryParse(view.CustomAccentColor, out Color customColor)
                ? customColor
                : designAccent);

        if (accent.HasValue)
        {
            theme.CustomAccentColor = accent;
        }

        AccentResolution.ApplyTextOnAccent(Resources, accent);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
