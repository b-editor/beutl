using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Beutl.Api.Services;

namespace Beutl.Editor.Components.Helpers;

public static class AppHelper
{
    public static Func<ContextCommandManager?>? GetContextCommandManager { get; set; }

    // Test seam: the headless test app has no application lifetime to find the main window through.
    internal static TopLevel? OverrideTopLevel { get; set; }

    public static TopLevel? GetTopLevel()
    {
        return OverrideTopLevel ?? (Application.Current?.ApplicationLifetime) switch
        {
            IClassicDesktopStyleApplicationLifetime desktop => desktop.MainWindow,
            _ => null,
        };
    }
}
