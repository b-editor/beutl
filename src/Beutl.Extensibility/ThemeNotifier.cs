using Beutl.Logging;

using Microsoft.Extensions.Logging;

namespace Beutl.Extensibility;

// Notifies the ThemeExtension that owns a descriptor of apply/revert. Pure (no Application access),
// so it is unit-testable; ThemeService calls it from its apply path.
public static class ThemeNotifier
{
    private static readonly ILogger s_logger = Log.CreateLogger(nameof(ThemeNotifier));

    public static void NotifyApplied(ThemeDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (ThemeRegistry.GetExtension(descriptor.Id) is { } extension)
        {
            try
            {
                extension.OnApplied(new ThemeApplyContext { Descriptor = descriptor });
            }
            catch (Exception ex)
            {
                s_logger.LogWarning(ex, "ThemeExtension.OnApplied for '{Id}' threw; continuing.", descriptor.Id);
            }
        }
    }

    public static void NotifyReverted(ThemeDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (ThemeRegistry.GetExtension(descriptor.Id) is { } extension)
        {
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
}
