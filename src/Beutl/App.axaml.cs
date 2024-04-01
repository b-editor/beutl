using System.Reactive.Concurrency;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Presenters;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.ReactiveUI;
using Avalonia.Styling;
using Avalonia.Threading;

using Beutl.Configuration;
using Beutl.NodeTree.Nodes;
using Beutl.Operators;
using Beutl.Services;
using Beutl.Services.StartupTasks;
using Beutl.ViewModels;
using Beutl.Views;

using FluentAvalonia.Core;
using FluentAvalonia.Styling;

using Reactive.Bindings;

namespace Beutl;

public sealed class App : Application
{
    private readonly TaskCompletionSource _windowOpenTcs = new();
    private FluentAvaloniaTheme? _theme;
    private MainViewModel? _mainViewModel;
    private Startup? _startUp;

    public override void Initialize()
    {
        _startUp = GetMainViewModel().RunStartupTask();
        _startUp.WaitAll().ContinueWith(_ => _startUp = null);

        using Activity? activity = Telemetry.StartActivity("App.Initialize");

        FAUISettings.SetAnimationsEnabledAtAppLevel(true);

        GlobalConfiguration config = GlobalConfiguration.Instance;
        ViewConfig view = config.ViewConfig;

        AvaloniaXamlLoader.Load(this);
        Resources["PaletteColors"] = AppHelpers.GetPaletteColors();

        _theme = (FluentAvaloniaTheme)Styles[0];

        activity?.AddEvent(new ActivityEvent("Xaml_Loaded"));

        view.GetObservable(ViewConfig.ThemeProperty).Subscribe(v =>
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                switch (v)
                {
                    case ViewConfig.ViewTheme.Light:
                        RequestedThemeVariant = ThemeVariant.Light;
                        break;
                    case ViewConfig.ViewTheme.Dark:
                        RequestedThemeVariant = ThemeVariant.Dark;
                        break;
                    case ViewConfig.ViewTheme.HighContrast:
                        RequestedThemeVariant = FluentAvaloniaTheme.HighContrastTheme;
                        break;
                    case ViewConfig.ViewTheme.System:
                        _theme.PreferSystemTheme = true;
                        break;
                }
            }, DispatcherPriority.Send);
        });

        if (view.UseCustomAccentColor && Color.TryParse(view.CustomAccentColor, out Color customColor))
        {
            activity?.SetTag("CustomAccentColor", customColor.ToString());

            _theme.CustomAccentColor = customColor;
        }

        if (OperatingSystem.IsMacOS())
        {
            SetTextAlignmentOverrides(_theme);
        }
    }

    // https://github.com/amwx/FluentAvalonia/blob/ac23275f71cbdb4640342d1ab57e1d30a5f82a91/src/FluentAvalonia/Styling/Core/FluentAvaloniaTheme.axaml.cs
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetTextAlignmentOverrides(FluentAvaloniaTheme theme)
    {
        Resources.Add("CheckBoxPadding", new Thickness(8, 5, 0, 5));
        Resources.Add("ComboBoxPadding", new Thickness(12, 5, 0, 5));
        Resources.Add("ComboBoxItemThemePadding", new Thickness(11, 5, 11, 5));
        Resources.Add("TextControlThemePadding", new Thickness(10, 8, 6, 6)); //10,5,6,6

        var s = new Style(x => x.OfType(typeof(CheckBox)));
        s.Setters.Add(new Setter(ContentControl.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        theme.Add(s);

        // Set Padding & VCA on RadioButton to center the content
        var s2 = new Style(x => x.OfType(typeof(RadioButton)));
        s2.Setters.Add(new Setter(ContentControl.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        s2.Setters.Add(new Setter(Decorator.PaddingProperty, new Thickness(8, 6, 0, 6)));
        theme.Add(s2);

        // Center the TextBlock in ComboBox
        // This is special - we only want to do this if the content is a string - otherwise custom content
        // may get messed up b/c of the centered alignment
        var s3 = new Style(x => x.OfType<ComboBox>().Template().OfType<ContentControl>().Child().OfType<TextBlock>());
        s3.Setters.Add(new Setter(Layoutable.VerticalAlignmentProperty, VerticalAlignment.Center));
        theme.Add(s3);
    }

    public override void RegisterServices()
    {
        using Activity? activity = Telemetry.StartActivity("App.RegisterServices");

        base.RegisterServices();

        PropertyEditorExtension.DefaultHandler = new PropertyEditorService.PropertyEditorExtensionImpl();
        ProjectItemContainer.Current.Generator = new ProjectItemGenerator();
        NotificationService.Handler = new NotificationServiceHandler();

        // 以下三つの処理は意外と重い
        Parallel.Invoke(
            () => GetMainViewModel().RegisterServices(),
            LibraryRegistrar.RegisterAll,
            NodesRegistrar.RegisterAll);

        ReactivePropertyScheduler.SetDefault(AvaloniaScheduler.Instance);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (OperatingSystem.IsMacOS())
            {
                desktop.MainWindow = new MacWindow
                {
                    DataContext = GetMainViewModel(),
                };
            }
            else
            {
                desktop.MainWindow = new MainWindow
                {
                    DataContext = GetMainViewModel(),
                };
            }

            desktop.MainWindow.Opened += (_, _) => _windowOpenTcs.SetResult();
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView
            {
                DataContext = GetMainViewModel(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    public static IClipboard? GetClipboard()
    {
        return GetTopLevel()?.Clipboard;
    }

    public static TopLevel? GetTopLevel()
    {
        return (Current?.ApplicationLifetime) switch
        {
            IClassicDesktopStyleApplicationLifetime desktop => desktop.MainWindow,
            ISingleViewApplicationLifetime { MainView: { } mainview } => TopLevel.GetTopLevel(mainview),
            _ => null,
        };
    }

    public static FluentAvaloniaTheme? GetFATheme()
    {
        return (Current as App)?._theme;
    }

    public static async ValueTask WaitStartupTask()
    {
        if (Current is App { _startUp: { } obj })
        {
            await obj.WaitAll();
        }
    }

    public static async ValueTask WaitLoadingExtensions()
    {
        if (Current is App { _startUp: { } obj })
        {
            await obj.WaitLoadingExtensions();
        }
    }

    public static async ValueTask WaitWindowOpened()
    {
        if (Current is App app)
        {
            await app._windowOpenTcs.Task.ConfigureAwait(false);
        }
    }

    private MainViewModel GetMainViewModel()
    {
        return _mainViewModel ??= new MainViewModel();
    }

    private void AboutBeutlClicked(object? sender, EventArgs e)
    {
        if (_mainViewModel != null)
        {
            _mainViewModel.SelectedPage.Value = _mainViewModel.SettingsPage;
            (_mainViewModel.SettingsPage.Context as SettingsPageViewModel)?.GoToSettingsPage();
        }
    }

    private void OpenSettingsClicked(object? sender, EventArgs e)
    {
        if (_mainViewModel != null)
        {
            _mainViewModel.SelectedPage.Value = _mainViewModel.SettingsPage;
            (_mainViewModel.SettingsPage.Context as SettingsPageViewModel)?.GoToAccountSettingsPage();
        }
    }
}
