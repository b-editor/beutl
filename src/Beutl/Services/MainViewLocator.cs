using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Beutl.Views;

namespace Beutl.Services;

internal static class MainViewLocator
{
    internal static MainView? Find()
    {
        IApplicationLifetime? lifetime = Application.Current?.ApplicationLifetime;

        if (lifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
        {
            if (desktopLifetime.MainWindow is MainWindow window)
            {
                return window.mainView;
            }
            else if (desktopLifetime.MainWindow is MacWindow mwindow)
            {
                return mwindow.mainView;
            }
        }
        else if (lifetime is ISingleViewApplicationLifetime singleViewLifetime)
        {
            return singleViewLifetime.MainView as MainView;
        }

        return null;
    }
}
