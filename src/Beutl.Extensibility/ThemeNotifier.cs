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

    public static void NotifyApplied(ThemeDescriptor descriptor, ThemeExtension? extension)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (extension == null)
        {
            return;
        }

        try
        {
            extension.OnApplied(new ThemeApplyContext { Descriptor = descriptor });
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(ex, "ThemeExtension.OnApplied for '{Id}' threw; continuing.", descriptor.Id);
        }
    }

    public static void NotifyReverted(ThemeDescriptor descriptor, ThemeExtension? extension)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (extension == null)
        {
            return;
        }

        try
        {
            extension.OnReverted();
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(ex, "ThemeExtension.OnReverted for '{Id}' threw; continuing.", descriptor.Id);
        }
    }
}
