using Avalonia;
using Avalonia.Markup.Xaml;
using Beutl.Media.Decoding;

namespace Beutl.E2ETests;

public sealed class TestApp : Application
{
    internal Beutl.Configuration.GlobalConfiguration Configuration { get; } = new();

    internal Beutl.Services.INotificationService Notifications { get; } = new Beutl.Services.NotificationService();
    internal DecoderRegistry Decoders { get; }
    internal Beutl.Media.FontManager Fonts { get; } = TestFonts.Manager;

    public TestApp()
    {
        Decoders = new DecoderRegistry(Configuration.ExtensionConfig);
    }

    public override void Initialize()
    {
        Beutl.Testing.Headless.ControlHost.ConfigureWindow = window =>
        {
            if (Beutl.Controls.ControlNotifications.GetNotifications(window) is null)
                Beutl.Controls.ControlNotifications.SetNotifications(window, Notifications);
            if (Beutl.Controls.ControlDecoders.GetDecoders(window) is null)
                Beutl.Controls.ControlDecoders.SetDecoders(window, Decoders);
            if (Beutl.Controls.ControlFonts.GetFonts(window) is null)
                Beutl.Controls.ControlFonts.SetFonts(window, Fonts);
            if (Beutl.Controls.ControlConfiguration.GetConfiguration(window) is null)
                Beutl.Controls.ControlConfiguration.SetConfiguration(window, Configuration);
        };
        AvaloniaXamlLoader.Load(this);
    }
}
