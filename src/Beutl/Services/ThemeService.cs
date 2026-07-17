using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Beutl.Configuration;
using Beutl.Extensibility;
using Beutl.Language;
using FluentAvalonia.Styling;
using Reactive.Bindings;

namespace Beutl.Services;

// Applies the selected theme id to the running app: resolves it via ThemeRegistry, sets
// RequestedThemeVariant (or PreferSystemTheme for the system theme), and merges the descriptor's
// brush-override resources. Re-applies on ViewConfig.Theme changes and ThemeRegistry.Changed.
internal sealed class ThemeService : IDisposable
{
    private readonly FluentAvaloniaTheme _theme;
    private readonly ViewConfig _viewConfig;
    private IResourceProvider? _currentResources;
    private ThemeDescriptor? _appliedDescriptor;
    private IDisposable? _themeSubscription;
    private bool _changedSubscribed;

    public ThemeService(FluentAvaloniaTheme theme, ViewConfig viewConfig)
    {
        _theme = theme;
        _viewConfig = viewConfig;
    }

    public void Start()
    {
        foreach (ThemeDescriptor descriptor in GetBuiltinThemes())
        {
            ThemeRegistry.Register(descriptor);
        }

        _themeSubscription = _viewConfig.GetObservable(ViewConfig.ThemeProperty)
            .Subscribe(ApplyTheme);
        ThemeRegistry.Changed += OnThemeRegistryChanged;
        _changedSubscribed = true;
        ApplyTheme(_viewConfig.Theme);
    }

    private static ThemeDescriptor[] GetBuiltinThemes() =>
    [
        new(BuiltinThemeIds.Light, SettingsStrings.Light, ThemeVariant.Light),
        new(BuiltinThemeIds.Dark, SettingsStrings.Dark, ThemeVariant.Dark),
        new(BuiltinThemeIds.HighContrast, SettingsStrings.HighContrast, FluentAvaloniaTheme.HighContrastTheme),
        new(BuiltinThemeIds.System, SettingsStrings.FollowSystem, ThemeVariant.Default, IsSystemFollowing: true),
    ];

    private void OnThemeRegistryChanged(object? sender, EventArgs e)
    {
        // The selected theme may have just been registered or removed (ResolveOrDefault falls back to Dark).
        ApplyTheme(_viewConfig.Theme);
    }

    private void ApplyTheme(string themeId)
    {
        ThemeDescriptor? descriptor = ThemeRegistry.ResolveOrDefault(themeId);
        if (descriptor == null)
        {
            return; // nothing registered yet (very early startup)
        }

        Dispatcher.UIThread.InvokeAsync(() => ApplyCore(descriptor), DispatcherPriority.Send);
    }

    private void ApplyCore(ThemeDescriptor descriptor)
    {
        // Skip when the resolved descriptor is unchanged — re-applying on unrelated
        // ThemeRegistry.Changed (extension load/unload) would flicker.
        if (_appliedDescriptor == descriptor)
        {
            return;
        }

        ThemeDescriptor? previous = _appliedDescriptor;
        _appliedDescriptor = descriptor;

        if (descriptor.IsSystemFollowing)
        {
            _theme.PreferSystemTheme = true;
        }
        else
        {
            _theme.PreferSystemTheme = false;
            Application.Current!.RequestedThemeVariant = descriptor.BaseVariant;
        }

        ApplyResources(descriptor);

        if (previous != null)
        {
            ThemeNotifier.NotifyReverted(previous);
        }

        ThemeNotifier.NotifyApplied(descriptor);
    }

    private void ApplyResources(ThemeDescriptor descriptor)
    {
        var merged = Application.Current!.Resources.MergedDictionaries;
        if (_currentResources != null)
        {
            merged.Remove(_currentResources);
            _currentResources = null;
        }

        if (descriptor.ResourceUri is { } uri)
        {
            if (AvaloniaXamlLoader.Load(uri, null) is IResourceProvider resource)
            {
                _currentResources = resource;
                merged.Add(resource);
            }
        }
    }

    public void Dispose()
    {
        _themeSubscription?.Dispose();
        if (_changedSubscribed)
        {
            ThemeRegistry.Changed -= OnThemeRegistryChanged;
        }
    }
}
