using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Beutl.Configuration;
using Beutl.Controls;
using Beutl.Editor.Components.TerminalTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Theming;

namespace Beutl.Editor.Components.TerminalTab.Views;

public partial class TerminalTabView : UserControl, IDisposable
{
    // RoyalTerminal 0.7.2 accepts typographic points. Avalonia font sizes are DIPs (96 per inch).
    private const double PointsPerDip = 72d / 96d;

    private static readonly ILogger s_logger = Log.CreateLogger<TerminalTabView>();
    private readonly CompositeDisposable _settingsSubscriptions = [];
    private readonly TerminalConfig _defaultSettings = new();
    private TerminalConfig? _settings;
    private TerminalTabViewModel? _sessionOwner;
    private bool _starting;
    private bool _disposed;

    public TerminalTabView()
    {
        InitializeComponent();
        BindSettings();
        Loaded += OnLoaded;
        Terminal.AddHandler(KeyDownEvent, (_, args) =>
        {
            if (args.Key == Key.Enter)
                UsageTelemetry.Current?.Record("tool.command", "Terminal", "SubmitInput");
        }, RoutingStrategies.Tunnel);
    }

    private void BindSettings()
    {
        if (_disposed || Terminal == null) return;

        TerminalConfig settings = (DataContext as TerminalTabViewModel)?.Settings
            ?? ControlConfiguration.GetConfiguration(this)?.TerminalConfig
            ?? _defaultSettings;
        if (ReferenceEquals(_settings, settings)) return;

        _settingsSubscriptions.Clear();
        _settings = settings;
        ApplySettings(settings);
    }

    private void ApplySettings(TerminalConfig settings)
    {
        settings.FontFamily.GetObservable().Subscribe(name =>
            Terminal.FontFamilyName = string.IsNullOrWhiteSpace(name)
                ? TerminalControl.FontFamilyNameProperty.GetDefaultValue(Terminal) : name.Trim())
            .DisposeWith(_settingsSubscriptions);
        settings.FontSize.GetObservable()
            .Subscribe(dips => Terminal.TerminalFontSize = dips * PointsPerDip)
            .DisposeWith(_settingsSubscriptions);
        settings.ScrollbackLines.GetObservable().Subscribe(lines => Terminal.ScrollbackLimit = lines).DisposeWith(_settingsSubscriptions);

        var applicationTheme = ObserveBrush("TerminalForegroundBrush").CombineLatest(
            ObserveBrush("DockSurfacePanelBrush"), ObserveBrush("DockSurfaceWorkbenchBrush"), CreateApplicationTheme);
        settings.ColorScheme.GetObservable().Select(scheme => scheme switch
            {
                TerminalColorScheme.Dark => Observable.Return(TerminalTheme.Dark),
                TerminalColorScheme.Light => Observable.Return(TerminalTheme.Light),
                _ => applicationTheme
            })
            .Switch().Subscribe(Terminal.ApplyTheme).DisposeWith(_settingsSubscriptions);
    }

    private IObservable<ISolidColorBrush> ObserveBrush(string key) =>
        this.GetResourceObservable(key).OfType<ISolidColorBrush>()
            .Select(brush => brush is SolidColorBrush mutable
                ? mutable.GetObservable(SolidColorBrush.ColorProperty)
                    .CombineLatest(mutable.GetObservable(Brush.OpacityProperty), (_, _) => brush)
                : Observable.Return(brush))
            .Switch();

    private static TerminalTheme CreateApplicationTheme(ISolidColorBrush foreground, ISolidColorBrush panel, ISolidColorBrush workbench)
    {
        // RoyalTerminal needs an opaque color. The dock paints its panel brush over the workbench.
        Color fill = panel.Color, behind = workbench.Color;
        double opacity = fill.A / 255d * panel.Opacity;
        byte Blend(byte fillChannel, byte behindChannel) => (byte)Math.Round(double.Lerp(behindChannel, fillChannel, opacity));
        Color background = Color.FromRgb(Blend(fill.R, behind.R), Blend(fill.G, behind.G), Blend(fill.B, behind.B));
        uint text = foreground.Color.ToUInt32();
        return new TerminalTheme(text, background.ToUInt32(), cursorColor: text, palette: TerminalTheme.Dark.Palette);
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        TryStartSession();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        BindSettings();
        TryStartSession();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ControlConfiguration.ConfigurationProperty)
            BindSettings();
    }

    private void TryStartSession()
    {
        // The dock keeps this view when switching tabs and may temporarily clear its DataContext.
        if (!_disposed && !_starting && IsLoaded && DataContext is TerminalTabViewModel owner && owner != _sessionOwner)
        {
            _ = StartSessionAsync(owner);
        }
    }

    private void OnRestartClick(object? sender, RoutedEventArgs e)
    {
        if (!_disposed && !_starting && DataContext is TerminalTabViewModel { IsProcessExited.Value: true } owner)
        {
            _ = StartSessionAsync(owner);
        }
    }

    private async Task StartSessionAsync(TerminalTabViewModel owner)
    {
        bool restart = owner == _sessionOwner;
        _starting = true;
        _sessionOwner = owner;
        owner.IsProcessExited.Value = false;
        owner.TerminalTitle.Value = null;
        try
        {
            if (Terminal.HasPty) Terminal.StopPty();

            var size = PixelSize.FromSize(Terminal.Bounds.Size, TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
            var (shell, args) = owner.GetShellCommand();
            var options = new PtyTransportOptions(
                new TerminalCommandSpec(shell, args),
                owner.WorkingDirectory ?? Environment.CurrentDirectory,
                owner.LangFallback is { } lang ? new Dictionary<string, string> { ["LANG"] = lang } : null,
                new TerminalSessionDimensions(Terminal.Columns, Terminal.Rows, size.Width, size.Height));
            await Terminal.StartSessionAsync(options, preserveScrollback: restart);
            if (!_disposed && IsLoaded && DataContext == owner) Terminal.Focus();
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(ex, "Could not start the terminal shell");
            if (!_disposed)
            {
                owner.IsProcessExited.Value = true;
                Terminal.WriteOutput(Encoding.UTF8.GetBytes(ex.Message + "\r\n"));
            }
        }
        finally
        {
            _starting = false;
            if (_disposed) Terminal.StopPty();
            else TryStartSession();
        }
    }

    private void OnTerminalTitleChanged(object? sender, string title)
    {
        if (_sessionOwner is { } owner) owner.TerminalTitle.Value = title;
    }

    private void OnProcessExited(object? sender, int exitCode)
    {
        if (_sessionOwner is { } owner) owner.IsProcessExited.Value = true;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _sessionOwner = null;
        Loaded -= OnLoaded;
        _settingsSubscriptions.Dispose();
        _settings = null;
        if (!_starting) Terminal.StopPty();
    }
}
