using System.Globalization;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;

using Beutl.Configuration;
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
            default:
                // PackageTools.UI loads no extensions/ThemeRegistry, so a custom theme id can't be
                // resolved here — fall back to Dark rather than carry an unknown variant.
                RequestedThemeVariant = ThemeVariant.Dark;
                break;
        }


        if (view.UseCustomAccentColor && Color.TryParse(view.CustomAccentColor, out Color customColor))
        {
            theme.CustomAccentColor = customColor;
        }
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
