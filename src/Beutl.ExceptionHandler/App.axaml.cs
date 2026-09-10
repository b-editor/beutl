using Avalonia;
#if DEBUG
using Avalonia.Diagnostics;
#endif
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Beutl.ExceptionHandler;

public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
#if DEBUG
        this.AttachDeveloperTools();
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            string? sessionId = null;
            string[] args = desktop.Args ?? [];
            int idx = Array.IndexOf(args, "--session-id");
            if (idx >= 0 && idx + 1 < args.Length)
            {
                sessionId = args[idx + 1];
            }

            desktop.MainWindow = new MainWindow(sessionId);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
