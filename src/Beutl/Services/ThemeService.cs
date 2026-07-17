using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Beutl.Configuration;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.Logging;
using FluentAvalonia.Styling;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.Services;

// Applies the selected theme id to the running app: resolves it via ThemeRegistry, sets
// RequestedThemeVariant (or PreferSystemTheme for the system theme), and merges the descriptor's
// brush-override resources. Re-applies on ViewConfig.Theme changes and ThemeRegistry.Changed.
internal sealed class ThemeService : IDisposable
{
    private static readonly ILogger s_logger = Log.CreateLogger<ThemeService>();
    private readonly FluentAvaloniaTheme _theme;
    private readonly ViewConfig _viewConfig;
    private IResourceProvider? _currentResources;
    private ThemeDescriptor? _appliedDescriptor;
    private ThemeExtension? _appliedExtension;
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
        // ThemeRegistry.Changed (extension load/unload) would flicker. Identity rather than the
        // record's structural equality, matching how ThemeRegistry keys ownership: an equal-valued
        // re-registration is a new owner that still needs its OnApplied. An untouched registry
        // resolves to the same instance, so this still skips.
        if (ReferenceEquals(_appliedDescriptor, descriptor))
        {
            return;
        }

        // Owner of this exact instance, resolved once: ThemeRegistry cannot map the id back to the
        // extension after it unregisters (which is when the revert notification is due), and by now
        // the id may belong to a replacement registered since ApplyTheme queued this call.
        ThemeExtension? extension = ThemeRegistry.GetOwner(descriptor);

        // ResourceUri is extension-controlled and may be missing or malformed. Load before touching
        // any state so a failure leaves the current theme intact — committing _appliedDescriptor
        // first would also make every later attempt at this descriptor a no-op.
        IResourceProvider? nextResources;
        try
        {
            nextResources = LoadResources(descriptor);
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(
                ex, "Failed to load resources for theme '{Id}'; keeping the current theme.", descriptor.Id);
            return;
        }

        ThemeDescriptor? previous = _appliedDescriptor;
        ThemeExtension? previousExtension = _appliedExtension;
        _appliedDescriptor = descriptor;
        _appliedExtension = extension;

        if (descriptor.IsSystemFollowing)
        {
            _theme.PreferSystemTheme = true;
        }
        else
        {
            _theme.PreferSystemTheme = false;
            Application.Current!.RequestedThemeVariant = descriptor.BaseVariant;
        }

        SwapResources(nextResources);

        if (previous != null)
        {
            ThemeNotifier.NotifyReverted(previous, previousExtension);
        }

        ThemeNotifier.NotifyApplied(descriptor, extension);
    }

    private static IResourceProvider? LoadResources(ThemeDescriptor descriptor)
    {
        if (descriptor.ResourceUri is not { } uri)
        {
            return null;
        }

        object? loaded = AvaloniaXamlLoader.Load(uri, null);
        if (loaded is IResourceProvider resources)
        {
            return resources;
        }

        // A root that is not a ResourceDictionary would otherwise be indistinguishable from "this
        // theme has no resources", which silently drops the previous theme's overrides. Throwing
        // routes it through the caller's catch, so the current theme survives and the extension
        // author gets the offending type.
        throw new InvalidOperationException(
            $"Theme '{descriptor.Id}' resource '{uri}' must be a ResourceDictionary (or another " +
            $"{nameof(IResourceProvider)}), but loaded as '{loaded?.GetType().FullName ?? "null"}'.");
    }

    private void SwapResources(IResourceProvider? next)
    {
        var merged = Application.Current!.Resources.MergedDictionaries;
        if (_currentResources != null)
        {
            merged.Remove(_currentResources);
        }

        _currentResources = next;
        if (next != null)
        {
            merged.Add(next);
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
