
using System.ComponentModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Avalonia.Threading;

using Beutl.Configuration;
using Beutl.ViewModels;

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
            Position = NotificationPosition.TopRight,
            Margin = new Thickness(64, 40, 0, 0)
        };
        TitleBar.Height = 40;

        ActualThemeVariantChanged += OnActualThemeVariantChanged;
#if DEBUG
        this.AttachDevTools();
#endif
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;
        OnThemeChanged(ActualThemeVariant, viewConfig.IsMicaEffectEnabled);
    }

    public WindowNotificationManager NotificationManager { get; }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        mainView.Focus();
        ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;

        viewConfig.GetObservable(ViewConfig.IsMicaEffectEnabledProperty).Subscribe(value
            => Dispatcher.UIThread.InvokeAsync(()
                => OnThemeChanged(ActualThemeVariant, value)));
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.Dispose();
        }
    }

    private void OnThemeChanged(ThemeVariant theme, bool isMicaEnabled)
    {
        if (theme == FluentAvaloniaTheme.HighContrastTheme)
        {
            SetValue(BackgroundProperty, AvaloniaProperty.UnsetValue);
        }
        else if (OperatingSystem.IsWindows())
        {
            if (IsWindows11 && isMicaEnabled)
            {
                TransparencyBackgroundFallback = Brushes.Transparent;
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Mica };

                TryEnableMicaEffect(theme);
            }
            else
            {
                TransparencyLevelHint = new[] { WindowTransparencyLevel.None };

                TryDisableMicaEffect(theme);
            }
        }
    }

    private void TryEnableMicaEffect(ThemeVariant thm)
    {
        // The background colors for the Mica brush are still based around SolidBackgroundFillColorBase resource
        // BUT since we can't control the actual Mica brush color, we have to use the window background to create
        // the same effect. However, we can't use SolidBackgroundFillColorBase directly since its opaque, and if
        // we set the opacity the color become lighter than we want. So we take the normal color, darken it and 
        // apply the opacity until we get the roughly the correct color
        // NOTE that the effect still doesn't look right, but it suffices. Ideally we need access to the Mica
        // CompositionBrush to properly change the color but I don't know if we can do that or not
        if (thm == ThemeVariant.Dark)
        {
            Color2 color = this.TryFindResource("SolidBackgroundFillColorBase", out object? value)
                ? (Color)value!
                : new Color2(32, 32, 32);

            color = color.LightenPercent(-0.8f);

            Background = new ImmutableSolidColorBrush(color, 0.9);
        }
        else if (thm == ThemeVariant.Light)
        {
            // Similar effect here
            Color2 color = this.TryFindResource("SolidBackgroundFillColorBase", out object? value)
                ? (Color)value!
                : new Color2(243, 243, 243);

            Background = new ImmutableSolidColorBrush(color, 0.9);
        }
    }

    private void TryDisableMicaEffect(ThemeVariant thm)
    {
        if (thm == ThemeVariant.Dark)
        {
            Color2 color = this.TryFindResource("SolidBackgroundFillColorBase", out object? value)
                ? (Color)value!
                : new Color2(32, 32, 32);

            color = color.LightenPercent(-0.8f);

            Background = new ImmutableSolidColorBrush(color);
        }
        else if (thm == ThemeVariant.Light)
        {
            // Similar effect here
            Color2 color = this.TryFindResource("SolidBackgroundFillColorBase", out object? value)
                ? (Color)value!
                : new Color2(243, 243, 243);

            Background = new ImmutableSolidColorBrush(color);
        }
    }
}
