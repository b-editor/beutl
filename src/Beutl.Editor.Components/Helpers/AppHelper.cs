using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace Beutl.Editor.Components.Helpers;

public static class AppHelper
{
    public static TopLevel? GetTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
        {
            return TopLevel.GetTopLevel(window);
        }

        return null;
    }
}
