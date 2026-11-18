using Avalonia;
using Beutl.Configuration;
using Beutl.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Helpers;

// A new window has no parent to inherit the attached properties that carry the app's services to its controls, so it
// takes them from the services or from the window that opens it.
internal static class HostServices
{
    public static void SetFrom(AvaloniaObject target, IServiceProvider services)
    {
        Set(
            target,
            services.GetRequiredService<ColorPaletteService>(),
            services.GetRequiredService<GlobalConfiguration>(),
            services.GetRequiredService<Beutl.Media.FontManager>(),
            services.GetRequiredService<INotificationService>());
    }

    public static void CopyFrom(AvaloniaObject target, AvaloniaObject owner)
    {
        Set(
            target,
            ColorPaletteService.GetService(owner),
            ControlConfiguration.GetConfiguration(owner),
            ControlFonts.GetFonts(owner),
            ControlNotifications.GetNotifications(owner));
    }

    private static void Set(
        AvaloniaObject target,
        ColorPaletteService? palettes,
        GlobalConfiguration? configuration,
        Beutl.Media.FontManager? fonts,
        INotificationService? notifications)
    {
        ColorPaletteService.SetService(target, palettes);
        ControlConfiguration.SetConfiguration(target, configuration);
        ControlFonts.SetFonts(target, fonts);
        ControlNotifications.SetNotifications(target, notifications);
    }
}
