
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Avalonia.Threading;

using Beutl.Configuration;

using FluentAvalonia.Styling;
using FluentAvalonia.UI.Media;
using FluentAvalonia.UI.Windowing;

namespace Beutl.Views;

public sealed partial class MainWindow : AppWindow
{
    public MainWindow()
    {
        InitializeComponent();

        NotificationManager = new WindowNotificationManager(this)
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Margin = new Thickness(64, 40, 0, 0)
        };
        TitleBar.Height = 40;
#if DEBUG
        this.AttachDevTools();
#endif
    }

    public WindowNotificationManager NotificationManager { get; }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        mainView.Focus();
        FluentAvaloniaTheme thm = AvaloniaLocator.Current.GetService<FluentAvaloniaTheme>()!;
        ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;
        Application.Current!.ActualThemeVariantChanged += (_, _) => OnThemeChanged(viewConfig.IsMicaEffectEnabled);

        viewConfig.GetObservable(ViewConfig.IsMicaEffectEnabledProperty).Subscribe(value
            => Dispatcher.UIThread.InvokeAsync(()
                => OnThemeChanged(value)));

        if (OperatingSystem.IsWindows())
        {
            thm.UseSystemFontOnWindows = false;

            thm.ForceWin32WindowToTheme(this);
        }
    }

    private void OnThemeChanged(bool isMicaEnabled)
    {
        var theme = Application.Current!.ActualThemeVariant;
        if (theme == FluentAvaloniaTheme.HighContrastTheme)
        {
            SetValue(BackgroundProperty, AvaloniaProperty.UnsetValue);
        }
        else if (OperatingSystem.IsWindows())
        {
            if (IsWindows11 && isMicaEnabled)
            {
                TransparencyBackgroundFallback = Brushes.Transparent;
                TransparencyLevelHint = WindowTransparencyLevel.Mica;

                TryEnableMicaEffect(theme);
            }
            else
            {
                TransparencyLevelHint = WindowTransparencyLevel.None;

                TryDisableMicaEffect(theme);
            }
        }
    }

    private void TryEnableMicaEffect(ThemeVariant theme)
    {
        // The background colors for the Mica brush are still based around SolidBackgroundFillColorBase resource
        // BUT since we can't control the actual Mica brush color, we have to use the window background to create
        // the same effect. However, we can't use SolidBackgroundFillColorBase directly since its opaque, and if
        // we set the opacity the color become lighter than we want. So we take the normal color, darken it and 
        // apply the opacity until we get the roughly the correct color
        // NOTE that the effect still doesn't look right, but it suffices. Ideally we need access to the Mica
        // CompositionBrush to properly change the color but I don't know if we can do that or not
        if (theme == ThemeVariant.Dark)
        {
            Color2 color = this.TryFindResource("SolidBackgroundFillColorBase", out object? value)
                ? (Color)value!
                : new Color2(32, 32, 32);

            color = color.LightenPercent(-0.8f);

            Background = new ImmutableSolidColorBrush(color, 0.78);
        }
        else if (theme == ThemeVariant.Light)
        {
            // Similar effect here
            Color2 color = this.TryFindResource("SolidBackgroundFillColorBase", out object? value)
                ? (Color)value!
                : new Color2(243, 243, 243);

            color = color.LightenPercent(0.5f);

            Background = new ImmutableSolidColorBrush(color, 0.9);
        }
    }

    private void TryDisableMicaEffect(ThemeVariant theme)
    {
        if (theme == ThemeVariant.Dark)
        {
            Color2 color = this.TryFindResource("SolidBackgroundFillColorBase", out object? value)
                ? (Color)value!
                : new Color2(32, 32, 32);

            color = color.LightenPercent(-0.8f);

            Background = new ImmutableSolidColorBrush(color);
        }
        else if (theme == ThemeVariant.Light)
        {
            // Similar effect here
            Color2 color = this.TryFindResource("SolidBackgroundFillColorBase", out object? value)
                ? (Color)value!
                : new Color2(243, 243, 243);

            color = color.LightenPercent(0.5f);

            Background = new ImmutableSolidColorBrush(color);
        }
    }
}
