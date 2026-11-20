using Avalonia;
using Beutl.Configuration;
using Beutl.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Media.Decoding;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;
using ExtensionGuard = Beutl.Api.Services.ExtensionGuard;

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
            ExtensionGuard.FindService<DecoderRegistry>(services),
            services.GetRequiredService<INotificationService>());
    }

    public static void CopyFrom(AvaloniaObject target, AvaloniaObject owner)
    {
        Set(
            target,
            ColorPaletteService.GetService(owner),
            ControlConfiguration.GetConfiguration(owner),
            ControlFonts.GetFonts(owner),
            ControlDecoders.GetDecoders(owner),
            ControlNotifications.GetNotifications(owner));
    }

    private static void Set(
        AvaloniaObject target,
        ColorPaletteService? palettes,
        GlobalConfiguration? configuration,
        Beutl.Media.FontManager? fonts,
        DecoderRegistry? decoders,
        INotificationService? notifications)
    {
        ColorPaletteService.SetService(target, palettes);
        ControlConfiguration.SetConfiguration(target, configuration);
        ControlFonts.SetFonts(target, fonts);
        ControlDecoders.SetDecoders(target, decoders);
        ControlNotifications.SetNotifications(target, notifications);
    }
}
