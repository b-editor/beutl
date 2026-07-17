using Beutl.Logging;

using Microsoft.Extensions.Logging;

namespace Beutl.Extensibility;

// Static registry of ThemeDescriptor instances. A ThemeExtension registers its descriptor at
// Load() and unregisters at Unload(); the host resolves the selected theme id and re-applies when
// the set changes. Ids are unique: a repeat Register for the same id overwrites, so a re-Load after
// a rolled-back partial init is idempotent. The optional ThemeExtension is kept alongside the
// descriptor so the host can notify the owning extension on apply/revert without a separate lookup.
public static class ThemeRegistry
{
    private static readonly ILogger s_logger = Log.CreateLogger(nameof(ThemeRegistry));
    private static readonly Dictionary<string, (ThemeDescriptor Descriptor, ThemeExtension? Extension)> s_themes = [];
    private static readonly object s_lock = new();

    /// <summary>
    /// Raised after the set of registered themes changes. The host re-applies the current theme so
    /// an extension that registers/unregisters a theme is reflected live. Fired outside the internal
    /// lock, so a handler may safely re-enter <see cref="Enumerate"/>/<see cref="Resolve"/>.
    /// </summary>
    public static event EventHandler? Changed;

    public static void Register(ThemeDescriptor descriptor, ThemeExtension? extension = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.IsSystemFollowing && descriptor.ResourceUri != null)
        {
            throw new ArgumentException(
                "A system-following theme cannot specify ResourceUri: the host applies the OS-resolved variant and cannot honor per-variant resources.",
                nameof(descriptor));
        }

        lock (s_lock)
        {
            s_themes[descriptor.Id] = (descriptor, extension);
        }

        RaiseChanged();
    }

    /// <summary>
    /// Removes <paramref name="descriptor"/> only when it is still the instance registered under its
    /// id, so an extension unloading after another one overwrote its id cannot evict the replacement.
    /// Identity is reference equality, not the record's structural equality: two owners may supply
    /// equal-valued descriptors, and only the one that registered may remove it.
    /// </summary>
    public static bool Unregister(ThemeDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        bool removed;
        lock (s_lock)
        {
            removed = s_themes.TryGetValue(descriptor.Id, out var entry)
                      && ReferenceEquals(entry.Descriptor, descriptor)
                      && s_themes.Remove(descriptor.Id);
        }

        if (removed)
            RaiseChanged();

        return removed;
    }

    public static ThemeDescriptor? Resolve(string? id)
    {
        if (string.IsNullOrEmpty(id))
            return null;

        lock (s_lock)
        {
            return s_themes.TryGetValue(id, out var entry) ? entry.Descriptor : null;
        }
    }

    // The ThemeExtension that registered the descriptor, or null for a host-registered built-in.
    public static ThemeExtension? GetExtension(string? id)
    {
        if (string.IsNullOrEmpty(id))
            return null;

        lock (s_lock)
        {
            return s_themes.TryGetValue(id, out var entry) ? entry.Extension : null;
        }
    }

    /// <summary>
    /// Resolves <paramref name="id"/>, falling back to the built-in Dark, then to the first
    /// registered non-system-following theme. Returns null only when nothing is registered yet
    /// (early startup, before the host registers the built-ins).
    /// </summary>
    public static ThemeDescriptor? ResolveOrDefault(string? id)
    {
        if (Resolve(id) is { } direct)
            return direct;

        if (Resolve(BuiltinThemeIds.Dark) is { } dark)
            return dark;

        lock (s_lock)
        {
            return s_themes.Values.FirstOrDefault(x => !x.Descriptor.IsSystemFollowing).Descriptor;
        }
    }

    public static IReadOnlyList<ThemeDescriptor> Enumerate()
    {
        lock (s_lock)
        {
            return s_themes.Values.Select(x => x.Descriptor).ToArray();
        }
    }

    // Isolate each subscriber: a throwing Changed handler (during extension load/unload) must not
    // abort the registry mutation or prevent the other subscribers — e.g. the host re-apply — from
    // running.
    private static void RaiseChanged()
    {
        if (Changed is not { } handler)
            return;

        foreach (Delegate subscriber in handler.GetInvocationList())
        {
            try
            {
                ((EventHandler)subscriber)(null, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                s_logger.LogWarning(ex, "A ThemeRegistry.Changed subscriber threw; continuing with the rest.");
            }
        }
    }
}
