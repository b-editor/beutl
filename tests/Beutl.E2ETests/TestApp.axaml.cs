using Avalonia;
using Avalonia.Markup.Xaml;

namespace Beutl.E2ETests;

public sealed class TestApp : Application
{
    internal Beutl.Configuration.GlobalConfiguration Configuration { get; } = new();

    internal Beutl.Services.INotificationService Notifications { get; } = new Beutl.Services.NotificationService();
    internal Beutl.Media.FontManager Fonts { get; } = TestFonts.Manager;

    public override void Initialize()
    {
        Beutl.Testing.Headless.ControlHost.ConfigureWindow = window =>
        {
            if (Beutl.Controls.ControlNotifications.GetNotifications(window) is null)
                Beutl.Controls.ControlNotifications.SetNotifications(window, Notifications);
            if (Beutl.Controls.ControlFonts.GetFonts(window) is null)
                Beutl.Controls.ControlFonts.SetFonts(window, Fonts);
            if (Beutl.Controls.ControlConfiguration.GetConfiguration(window) is null)
                Beutl.Controls.ControlConfiguration.SetConfiguration(window, Configuration);
        };
        AvaloniaXamlLoader.Load(this);
    }
}
