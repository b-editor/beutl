using Avalonia.Threading;

namespace Beutl.Editor.Components.VersionControl.ViewModels;

internal static class VersionControlUiThread
{
    internal static void Post(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }
}
