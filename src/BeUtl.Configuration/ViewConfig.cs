using System.Globalization;

namespace BeUtl.Configuration;

public sealed class ViewConfig : ConfigurationBase
{
    public static readonly CoreProperty<ViewTheme> ThemeProperty;
    public static readonly CoreProperty<CultureInfo> UICultureProperty;

    static ViewConfig()
    {
        ThemeProperty = ConfigureProperty<ViewTheme, ViewConfig>("Theme")
            .SerializeName("theme")
            .DefaultValue(ViewTheme.System)
            .Observability(PropertyObservability.Changed)
            .Register();

        UICultureProperty = ConfigureProperty<CultureInfo, ViewConfig>("UICulture")
            .SerializeName("ui-culture")
            .DefaultValue(CultureInfo.InstalledUICulture)
            .Observability(PropertyObservability.Changed)
            .Register();
    }

    public ViewTheme Theme
    {
        get => GetValue(ThemeProperty);
        set => SetValue(ThemeProperty, value);
    }

    public CultureInfo UICulture
    {
        get => GetValue(UICultureProperty);
        set => SetValue(UICultureProperty, value);
    }

    public enum ViewTheme
    {
        Light,
        Dark,
        HighContrast,
        System
    }
}
