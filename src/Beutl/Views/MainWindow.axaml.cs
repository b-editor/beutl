
using System.ComponentModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Media;
using Avalonia.Media.Immutable;
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
        thm.RequestedThemeChanged += (_, e) => OnThemeChanged(e.NewTheme, viewConfig.IsMicaEffectEnabled);

        viewConfig.GetObservable(ViewConfig.IsMicaEffectEnabledProperty).Subscribe(value
            => Dispatcher.UIThread.InvokeAsync(()
                => OnThemeChanged(thm.RequestedTheme, value)));

        if (OperatingSystem.IsWindows())
        {
            thm.UseSystemFontOnWindows = false;

            thm.ForceWin32WindowToTheme(this);
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.CloseProject.Execute();
        }
    }

    private void OnThemeChanged(string theme, bool isMicaEnabled)
    {
        if (theme == FluentAvaloniaTheme.HighContrastModeString)
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

    private void TryEnableMicaEffect(string thm)
    {
        // The background colors for the Mica brush are still based around SolidBackgroundFillColorBase resource
        // BUT since we can't control the actual Mica brush color, we have to use the window background to create
        // the same effect. However, we can't use SolidBackgroundFillColorBase directly since its opaque, and if
        // we set the opacity the color become lighter than we want. So we take the normal color, darken it and 
        // apply the opacity until we get the roughly the correct color
        // NOTE that the effect still doesn't look right, but it suffices. Ideally we need access to the Mica
        // CompositionBrush to properly change the color but I don't know if we can do that or not
        if (thm == FluentAvaloniaTheme.DarkModeString)
        {
            Color2 color = this.TryFindResource("SolidBackgroundFillColorBase", out object? value)
                ? (Color)value!
                : new Color2(32, 32, 32);

            color = color.LightenPercent(-0.8f);

            Background = new ImmutableSolidColorBrush(color, 0.9);
        }
        else if (thm == FluentAvaloniaTheme.LightModeString)
        {
            // Similar effect here
            Color2 color = this.TryFindResource("SolidBackgroundFillColorBase", out object? value)
                ? (Color)value!
                : new Color2(243, 243, 243);

            Background = new ImmutableSolidColorBrush(color, 0.9);
        }
    }

    private void TryDisableMicaEffect(string thm)
    {
        if (thm == FluentAvaloniaTheme.DarkModeString)
        {
            Color2 color = this.TryFindResource("SolidBackgroundFillColorBase", out object? value)
                ? (Color)value!
                : new Color2(32, 32, 32);

            color = color.LightenPercent(-0.8f);

            Background = new ImmutableSolidColorBrush(color);
        }
        else if (thm == FluentAvaloniaTheme.LightModeString)
        {
            // Similar effect here
            Color2 color = this.TryFindResource("SolidBackgroundFillColorBase", out object? value)
                ? (Color)value!
                : new Color2(243, 243, 243);

            Background = new ImmutableSolidColorBrush(color);
        }
    }
}
