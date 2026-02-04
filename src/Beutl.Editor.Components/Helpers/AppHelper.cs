using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Beutl.Api.Services;

namespace Beutl.Editor.Components.Helpers;

public static class AppHelper
{
    public static Func<ContextCommandManager?>? GetContextCommandManager { get; set; }

    public static TopLevel? GetTopLevel()
    {
        return (Application.Current?.ApplicationLifetime) switch
        {
            IClassicDesktopStyleApplicationLifetime desktop => desktop.MainWindow,
            ISingleViewApplicationLifetime { MainView: { } mainview } => TopLevel.GetTopLevel(mainview),
            _ => null,
        };
    }
}
