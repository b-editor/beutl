using Avalonia.Media;

using Beutl.Logging;

using Microsoft.Extensions.Logging;

namespace Beutl.Extensibility;

// Notifies the ThemeExtension that owns a descriptor of apply/revert. Pure (no Application access),
// so it is unit-testable; ThemeService calls it from its apply path.
//
// The owner is a parameter rather than a ThemeRegistry lookup because by revert time the id may be
// unregistered (owner gone, OnReverted skipped) or re-registered by a different extension (which
// would receive callbacks for a descriptor it never supplied). Callers capture it at apply time.
public static class ThemeNotifier
{
    private static readonly ILogger s_logger = Log.CreateLogger(nameof(ThemeNotifier));

    public static void NotifyApplied(ThemeDescriptor descriptor, ThemeExtension? extension, Color? accent = null)
    {
        Notify(descriptor, extension, "ThemeExtension.OnApplied for '{Id}' threw; continuing.",
            e => e.OnApplied(new ThemeApplyContext { Descriptor = descriptor, Accent = accent }));
    }

    public static void NotifyAccentChanged(ThemeDescriptor descriptor, ThemeExtension? extension, Color? accent)
    {
        Notify(descriptor, extension, "ThemeExtension.OnAccentChanged for '{Id}' threw; continuing.",
            e => e.OnAccentChanged(new ThemeApplyContext { Descriptor = descriptor, Accent = accent }));
    }

    public static void NotifyReverted(ThemeDescriptor descriptor, ThemeExtension? extension)
    {
        Notify(descriptor, extension, "ThemeExtension.OnReverted for '{Id}' threw; continuing.",
            e => e.OnReverted());
    }

    // Each caller passes its own message template, keeping one constant template per callback.
    private static void Notify(
        ThemeDescriptor descriptor,
        ThemeExtension? extension,
        string messageTemplate,
        Action<ThemeExtension> invoke)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (extension == null)
        {
            return;
        }

        try
        {
            invoke(extension);
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(ex, messageTemplate, descriptor.Id);
        }
    }
}
