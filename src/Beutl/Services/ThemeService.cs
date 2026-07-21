using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
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
// Also the sole writer of FluentAvaloniaTheme.CustomAccentColor: the user's custom accent (when
// enabled) wins, then the applied descriptor's AccentColor, then null (the OS accent).
internal sealed class ThemeService : IDisposable
{
    private static readonly ILogger s_logger = Log.CreateLogger<ThemeService>();
    private readonly FluentAvaloniaTheme _theme;
    private readonly ViewConfig _viewConfig;
    private IResourceProvider? _currentResources;
    private ThemeDescriptor? _appliedDescriptor;
    private ThemeExtension? _appliedExtension;
    private IDisposable? _themeSubscription;
    private IDisposable? _useCustomAccentSubscription;
    private IDisposable? _customAccentColorSubscription;
    private bool _changedSubscribed;
    private int _applyQueued;

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
            .Subscribe(_ => ScheduleApply());
        _useCustomAccentSubscription = _viewConfig.GetObservable(ViewConfig.UseCustomAccentColorProperty)
            .Subscribe(_ => ScheduleApply());
        _customAccentColorSubscription = _viewConfig.GetObservable(ViewConfig.CustomAccentColorProperty)
            .Subscribe(_ => ScheduleApply());
        ThemeRegistry.Changed += OnThemeRegistryChanged;
        _changedSubscribed = true;
        ScheduleApply();
    }

    private static ThemeDescriptor[] GetBuiltinThemes() =>
    [
        new(BuiltinThemeIds.Light, SettingsStrings.Light, ThemeVariant.Light),
        // "Classic" distinguishes FluentAvalonia's stock dark from the default DarkBorderThemeExtension,
        // which also shows as "Dark" but ships the near-black design overrides.
        new(BuiltinThemeIds.Dark, SettingsStrings.DarkClassic, ThemeVariant.Dark),
        new(BuiltinThemeIds.HighContrast, SettingsStrings.HighContrast, FluentAvaloniaTheme.HighContrastTheme),
        new(BuiltinThemeIds.System, SettingsStrings.FollowSystem, ThemeVariant.Default, IsSystemFollowing: true),
    ];

    // The selected theme may have just been registered or removed (ResolveOrDefault falls back to Dark).
    private void OnThemeRegistryChanged(object? sender, EventArgs e) => ScheduleApply();

    // Every trigger wants the same thing — apply whatever the config now names — so a burst of them
    // (Parallel.ForEach extension loading registers themes one by one) collapses into a single
    // pending job instead of one Send-priority callback each.
    private void ScheduleApply()
    {
        if (Interlocked.Exchange(ref _applyQueued, 1) != 0)
        {
            return;
        }

        Dispatcher.UIThread.Post(ResolveAndApply, DispatcherPriority.Send);
    }

    // Both the config value and the registry are read here rather than at schedule time: either can
    // change between the trigger and this callback, and only the state at apply time is right.
    private void ResolveAndApply()
    {
        Interlocked.Exchange(ref _applyQueued, 0);

        ApplySelectedTheme();
        // Unconditionally: an accent-config trigger arrives with the applied descriptor unchanged,
        // and a theme trigger can change which descriptor supplies the accent.
        ApplyAccent();
    }

    private void ApplySelectedTheme()
    {
        if (ThemeRegistry.ResolveOrDefault(_viewConfig.Theme) is not { } descriptor)
        {
            return; // nothing registered yet (very early startup)
        }

        if (ApplyCore(descriptor))
        {
            return;
        }

        // The selected theme could not be applied. Keeping the current one is right only while it is
        // still registered — at first apply there is none (the app would stay unthemed, and nothing
        // guarantees another trigger), and a failed candidate may have replaced the applied
        // descriptor's id, which would leave an evicted theme active with its owner never reverted.
        if (_appliedDescriptor != null
            && ReferenceEquals(ThemeRegistry.Resolve(_appliedDescriptor.Id), _appliedDescriptor))
        {
            return;
        }

        if (ThemeRegistry.Resolve(BuiltinThemeIds.Dark) is { } fallback
            && !ReferenceEquals(fallback, descriptor))
        {
            ApplyCore(fallback);
        }
    }

    // Skips writes of an unchanged value: every CustomAccentColor set makes FluentAvaloniaTheme
    // regenerate its SystemAccentColor shade resources and invalidate dependents.
    private void ApplyAccent()
    {
        Color? accent =
            _viewConfig.UseCustomAccentColor && Color.TryParse(_viewConfig.CustomAccentColor, out Color custom)
                ? custom
                : _appliedDescriptor?.AccentColor;

        if (_theme.CustomAccentColor != accent)
        {
            _theme.CustomAccentColor = accent;
        }
    }

    // False when the descriptor could not be applied and the caller should fall back; skipping an
    // already-applied descriptor counts as applied.
    private bool ApplyCore(ThemeDescriptor descriptor)
    {
        // Skip when the resolved descriptor is unchanged — re-applying on unrelated
        // ThemeRegistry.Changed (extension load/unload) would flicker. Identity rather than the
        // record's structural equality, matching how ThemeRegistry keys ownership: an equal-valued
        // re-registration is a new owner that still needs its OnApplied. An untouched registry
        // resolves to the same instance, so this still skips.
        if (ReferenceEquals(_appliedDescriptor, descriptor))
        {
            return true;
        }

        // Owner of this exact instance: ThemeRegistry cannot map the id back to the extension after
        // it unregisters, which is when the revert notification is due, and an id lookup could by
        // then belong to a replacement registered on a background thread.
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
            s_logger.LogWarning(ex, "Failed to load resources for theme '{Id}'.", descriptor.Id);
            return false;
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
        return true;
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
        _useCustomAccentSubscription?.Dispose();
        _customAccentColorSubscription?.Dispose();
        if (_changedSubscribed)
        {
            ThemeRegistry.Changed -= OnThemeRegistryChanged;
        }
    }
}
